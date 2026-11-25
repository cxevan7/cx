using System;
using System.Runtime.InteropServices;
using CX.Framework;
using CX.Graphics;

namespace CX.Alloy.Db;

/// <summary>
/// alloy.upscale on PLT textures (armour and clothing parts): the (intensity, layer) pairs rebuilt at 2x or 4x. The
/// layer is an index into the palette rows and never blends, so each output takes its nearest source texel's layer;
/// intensity is filtered within that layer only. A head's PLT is left as it is.
/// </summary>
internal static unsafe partial class TexRegistry
{
    /// <summary>A cache-key salt per source kind, so a TGA or PLT rebuild never answers for a DDS one of the same bytes.</summary>
    private const ulong UpscaleKindTga = 0x7467, UpscaleKindPlt = 0x706c;

    /// <summary>A head's texture: faces keep their own look, whatever the armour around them takes.</summary>
    internal static bool HeadTexture(string? resref) => resref != null && resref.Contains("_head", StringComparison.OrdinalIgnoreCase);

    /// <summary>Does alloy.upscale cover PLT job <paramref name="j"/>: any PLT but a head's, within the size cap.</summary>
    private static bool UpscalePltWanted(Job j)
    {
        int cap = Renderer.Upscale.V;
        return cap > 0 && j.Kind == TexKind.Plt && !j.Upgrade && !j.Tail && !HeadTexture(j.Resref)
               && Math.Max(j.W, j.H) <= cap && Math.Min(j.W, j.H) >= UpscaleMinSide;
    }

    /// <summary>A PLT file's level 0 pairs in a managed array, unpacked when the file is RLE; null when it does not parse.</summary>
    private static byte[]? PltLevel0(ReadOnlySpan<byte> raw)
    {
        if (!PltImage.Parse(raw, out int w, out int h, out _, out bool rle, out _)) return null;
        byte[] pairs = new byte[w * h * PltImage.BytesPerPixel];
        if (rle) return PltImage.Unrle(raw[PltImage.HeaderBytes..], pairs, out _) ? pairs : null;
        raw.Slice(PltImage.HeaderBytes, pairs.Length).CopyTo(pairs);
        return pairs;
    }

    /// <summary>A decoded PLT job swapped for its cached rebuild, or queued for one. <paramref name="raw"/> is the file.</summary>
    private static void UpscalePlt(Job j, ReadOnlySpan<byte> raw)
    {
        if (!UpscalePltWanted(j) || PltLevel0(raw) is not { } pairs) return;
        ulong key = UpscaleKindOf(UpscaleKey(pairs, j, DdsReader.Fmt.Bc1), UpscaleKindPlt);
        if (!UpscaleCached(j, key, out int w2, out int h2, out int levels, out NativeBuffer<byte> chain)) { QueueUpgradeFrom(j, pairs, DdsReader.Fmt.Bc1, key); return; }
        NativeMemory.Free(j.Data);
        j.Data = chain.Pointer; j.Native = true;
        j.W = w2; j.H = h2; j.Levels = levels; j.Bytes = chain.Length;
    }

    /// <summary>The PLT rebuild on the upgrade thread: pairs at 2x (layer-aware Lanczos), the AI's intensity where every
    /// neighbour shares a layer, the RG8 chain mipped as the decoder mips it, cached and left on the job.</summary>
    private static void PltWork(Job j)
    {
        byte[] src = j.UpSource!;
        j.UpSource = null;
        bool wrap = !j.Clamp;
        int w = j.W, h = j.H;
        byte[] level = PltUp2x(src, w, h, wrap);
        int factor = 2;
        int want = UpscaleFactor(w, h, true);
        byte[]? ai = AiUpscale(PltGrey(src, w, h), w, h, wrap, PltGrey(level, w * 2, h * 2), want, false);
        if (ai != null)
        {
            byte[] classic = want == 4 ? PltUp2x(level, w * 2, h * 2, wrap) : level;
            level = PltFromAi(src, w, h, wrap, ai, classic, want);
            factor = want;
        }
        int w2 = w * factor, h2 = h * factor, levels = Levels(w2, h2);
        long bytes = PltImage.ChainBytes(w2, h2, levels);
        var chain = new NativeBuffer<byte>();
        chain.TakeExact(checked((int)bytes));
        level.AsSpan().CopyTo(chain.Span);
        PltImage.BuildMips(chain.Pointer, w2, h2, levels);
        byte[] file = new byte[UpscaleHeader + bytes];
        BitConverter.TryWriteBytes(file.AsSpan(0, 4), w2);
        BitConverter.TryWriteBytes(file.AsSpan(4, 4), h2);
        BitConverter.TryWriteBytes(file.AsSpan(8, 4), levels);
        chain.Span.CopyTo(file.AsSpan(UpscaleHeader));
        string path = UpscalePath(j.Resref, UpscaleKeyAi(j.UpKey, ai != null, factor));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        CacheFile.Write(path, file);
        j.Data = chain.Pointer; j.Native = true;
        j.UpNote = $"upscaled PLT {w}x{h} -> {w2}x{h2} ({(ai != null ? $"AI {AiModelNames[AiModelWanted]}, {factor}x" : "classic 2x")})";
        j.W = w2; j.H = h2; j.Levels = levels; j.Bytes = chain.Length;
    }

    /// <summary>PLT pairs at twice their size. Each output takes its nearest source texel's layer, and its intensity is
    /// Lanczos-2 over the taps of that layer alone, clamped to that layer's nearest four source intensities.</summary>
    internal static byte[] PltUp2x(byte[] src, int w, int h, bool wrap)
    {
        Span<PltImage.Texel> s = MemoryMarshal.Cast<byte, PltImage.Texel>(src.AsSpan());
        int w2 = w * 2, h2 = h * 2;
        byte[] dst = new byte[w2 * h2 * PltImage.BytesPerPixel];
        Span<PltImage.Texel> d = MemoryMarshal.Cast<byte, PltImage.Texel>(dst.AsSpan());
        Span<float> we = stackalloc float[4], wo = stackalloc float[4];
        Taps(0.75f, we);
        Taps(0.25f, wo);
        int[][] tx = TapIndex(w2, w, wrap), ty = TapIndex(h2, h, wrap);
        int[] nx = NearIndex(w2, w, wrap), ny = NearIndex(h2, h, wrap);
        for (int y = 0; y < h2; y++)
        {
            Span<float> ky = (y & 1) == 0 ? we : wo;
            int sy = y >> 1;
            for (int x = 0; x < w2; x++)
            {
                Span<float> kx = (x & 1) == 0 ? we : wo;
                int sx = x >> 1;
                byte layer = s[Ix(sx, sy, w)].Layer;
                float sum = 0f, wsum = 0f;
                for (int a = 0; a < 4; a++)
                    for (int b = 0; b < 4; b++)
                    {
                        PltImage.Texel t = s[Ix(tx[b][x], ty[a][y], w)];
                        if (t.Layer != layer) continue;
                        float k = ky[a] * kx[b];
                        sum += k * t.Intensity; wsum += k;
                    }
                float lo = 255f, hi = 0f;
                foreach (int n in (ReadOnlySpan<int>)[Ix(sx, sy, w), Ix(nx[x], sy, w), Ix(sx, ny[y], w), Ix(nx[x], ny[y], w)])
                    if (s[n].Layer == layer) { lo = Math.Min(lo, s[n].Intensity); hi = Math.Max(hi, s[n].Intensity); }
                float v = wsum > 1e-4f ? Math.Clamp(sum / wsum, lo, hi) : s[Ix(sx, sy, w)].Intensity;
                d[Ix(x, y, w2)] = new PltImage.Texel { Intensity = (byte)(v + 0.5f), Layer = layer };
            }
        }
        return dst;
    }

    /// <summary>The AI's grey output at <paramref name="factor"/> as PLT pairs: each texel takes its source texel's layer,
    /// and the AI's intensity only where all nine source neighbours share that layer; at a layer's edge, the classic one.</summary>
    private static byte[] PltFromAi(byte[] src, int w, int h, bool wrap, byte[] ai, byte[] classic, int factor)
    {
        Span<PltImage.Texel> s = MemoryMarshal.Cast<byte, PltImage.Texel>(src.AsSpan());
        Span<PltImage.Texel> c = MemoryMarshal.Cast<byte, PltImage.Texel>(classic.AsSpan());
        int wf = w * factor, hf = h * factor;
        byte[] dst = new byte[wf * hf * PltImage.BytesPerPixel];
        Span<PltImage.Texel> d = MemoryMarshal.Cast<byte, PltImage.Texel>(dst.AsSpan());
        for (int y = 0; y < hf; y++)
            for (int x = 0; x < wf; x++)
            {
                int sx = x / factor, sy = y / factor;
                byte layer = s[Ix(sx, sy, w)].Layer;
                bool inside = true;
                for (int dy = -1; dy <= 1 && inside; dy++)
                    for (int dx = -1; dx <= 1 && inside; dx++)
                        inside = s[Ix(Edge(sx + dx, w, wrap), Edge(sy + dy, h, wrap), w)].Layer == layer;
                byte inten = inside ? ai[At(x, y, wf, 0)] : c[Ix(x, y, wf)].Intensity;
                d[Ix(x, y, wf)] = new PltImage.Texel { Intensity = inten, Layer = layer };
            }
        return dst;
    }

    /// <summary>PLT pairs as an opaque RGBA8 grey image of their intensity: what the AI model reads.</summary>
    private static byte[] PltGrey(byte[] pairs, int w, int h)
    {
        Span<PltImage.Texel> s = MemoryMarshal.Cast<byte, PltImage.Texel>(pairs.AsSpan());
        byte[] rgba = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte v = s[Ix(x, y, w)].Intensity;
                int o = At(x, y, w, 0);
                rgba[o] = v; rgba[o + 1] = v; rgba[o + 2] = v; rgba[o + 3] = 255;
            }
        return rgba;
    }

    /// <summary>Texel (<paramref name="x"/>, <paramref name="y"/>)'s index in a <paramref name="w"/> wide image.</summary>
    private static int Ix(int x, int y, int w) => y * w + x;

    /// <summary>Off-device: two layers side by side never blend at their edge, a gradient stays inside its range and keeps
    /// its layer, and a head's PLT is never covered.</summary>
    private static void PltUpscaleSelfCheck(Checker check)
    {
        const int n = 8;
        byte[] split = new byte[n * n * PltImage.BytesPerPixel];
        Span<PltImage.Texel> s = MemoryMarshal.Cast<byte, PltImage.Texel>(split.AsSpan());
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                s[Ix(x, y, n)] = x < n / 2 ? new PltImage.Texel { Intensity = 30, Layer = 2 } : new PltImage.Texel { Intensity = 220, Layer = 7 };
        Span<PltImage.Texel> up = MemoryMarshal.Cast<byte, PltImage.Texel>(PltUp2x(split, n, n, false).AsSpan());
        bool kept = true;
        for (int y = 0; y < n * 2; y++)
            for (int x = 0; x < n * 2; x++)
            {
                PltImage.Texel t = up[Ix(x, y, n * 2)], src = s[Ix(x / 2, y / 2, n)];
                kept &= t.Layer == src.Layer && t.Intensity == src.Intensity;
            }
        check.Expect(kept, "two PLT layers side by side upscale with no intensity crossing their edge and each texel's own layer");
        byte[] ramp = new byte[n * n * PltImage.BytesPerPixel];
        Span<PltImage.Texel> r = MemoryMarshal.Cast<byte, PltImage.Texel>(ramp.AsSpan());
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++) r[Ix(x, y, n)] = new PltImage.Texel { Intensity = (byte)(40 + x * 20), Layer = 4 };
        Span<PltImage.Texel> ru = MemoryMarshal.Cast<byte, PltImage.Texel>(PltUp2x(ramp, n, n, false).AsSpan());
        bool inside = true;
        foreach (PltImage.Texel t in ru) inside &= t.Layer == 4 && t.Intensity >= 40 && t.Intensity <= 40 + (n - 1) * 20;
        check.Expect(inside, "a PLT intensity ramp upscales inside its own range, on its own layer");
        check.Expect(HeadTexture("pfh0_head001") && HeadTexture("PMH0_HEAD012") && !HeadTexture("pmh0_chest004") && !HeadTexture(null),
                     "a head's PLT is told from an armour part's");
    }
}
