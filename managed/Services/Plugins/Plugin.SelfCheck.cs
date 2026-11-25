using System.Reflection;
using System.Text;
using CX.Alloy;
using CX.Framework;
using CX.Graphics;
using CX.Services.Update;

namespace CX.Services.Plugins;

/// <summary>The manifest, the stage rules, the signed index and the folder loader. Oracle: the example plugin under
/// <c>examples/plugins</c>, an index ssh-keygen signed over it with a throwaway key, and hand-written refusals.</summary>
internal static class PluginSelfCheck
{
    [SelfCheckSuite(SuitePhase.BeforeServices)]
    internal static bool SelfCheck(ExtenderLog log)
    {
        var check = new Checker(log, "Plugin.SelfCheck");
        byte[] json = Res("plugins/vignette/plugin"), stage = Res("plugins/vignette/stage");
        PluginManifest? m = PluginManifest.Parse(json, out string why);
        check.Expect(m is { Id: "vignette", Version: "1.0.0", Stage: "stage.glsl" } && m.Knobs.Length == 2 && m.Knobs[1].Uniform == "uSize",
                     $"the example manifest reads ({why})");
        Manifests(check);
        Stages(check, Encoding.ASCII.GetString(stage));
        Index(check, json, stage);
        Folder(check, json, stage);
        Compiles(check, Encoding.ASCII.GetString(stage), m);
        log.Info(check.Ok ? "Plugin.SelfCheck: passed" : "Plugin.SelfCheck: FAILED");
        return check.Ok;
    }

    private static void Manifests(Checker check)
    {
        const string ok = "\"id\": \"x1\", \"version\": \"1.0.0\", \"kind\": \"shader\", \"name\": \"X\", \"license\": \"MIT\", \"stage\": \"s.glsl\"";
        const string knob = "{ \"key\": \"a\", \"label\": \"A\", \"min\": 0, \"max\": 1, \"step\": 0.1, \"default\": 0.5, \"uniform\": \"uA\" }";
        (string Json, string Why)[] refused =
        [
            ("{" + ok + ", \"extra\": 1}", "unknown field"),
            ("{" + ok.Replace("\"x1\"", "\"X1\"") + "}", "id must"),
            ("{" + ok.Replace("\"shader\"", "\"native\"") + "}", "kind must"),
            ("{" + ok.Replace("\"s.glsl\"", "\"../s.glsl\"") + "}", "stage must"),
            ("{" + ok.Replace("\"1.0.0\"", "\"1.0\"") + "}", "version must"),
            ("{" + ok + ", \"knobs\": [" + knob.Replace("\"default\": 0.5", "\"default\": 2") + "]}", "range"),
            ("{" + ok + ", \"knobs\": [" + knob.Replace("\"uA\"", "\"uSrc\"") + "]}", "uniform must"),
            ("{" + ok + ", \"knobs\": [" + knob + ", " + knob.Replace("\"a\"", "\"b\"") + "]}", "repeats"),
            ("{" + ok + ", \"knobs\": [" + string.Join(", ", Enumerable.Repeat(knob, PluginManifest.MaxKnobs + 1)) + "]}", "at most"),
            ("[1]", "not an object"),
        ];
        check.Expect(PluginManifest.Parse(Encoding.UTF8.GetBytes("{" + ok + ", \"knobs\": [" + knob + "]}"), out string why) != null,
                     $"a minimal manifest with one knob reads ({why})");
        foreach ((string j, string w) in refused)
            check.Expect(PluginManifest.Parse(Encoding.UTF8.GetBytes(j), out why) == null && why.Contains(w, StringComparison.Ordinal),
                         $"refused for '{w}': {j[..Math.Min(j.Length, 60)]} ({why})");
    }

    private static void Stages(Checker check, string example)
    {
        check.Expect(PluginGlsl.Check(example) == null, $"the example stage passes ({PluginGlsl.Check(example)})");
        check.Expect(PluginGlsl.Check("// a # in a comment\nvec4 CxStage(vec4 c) { return c; }") == null, "a # inside a comment is no directive");
        (string Body, string Why)[] refused =
        [
            ("#extension GL_EXT_x : require\nvec4 CxStage(vec4 c) { return c; }", "preprocessor"),
            ("layout(binding = 3) uniform sampler2D t;\nvec4 CxStage(vec4 c) { return c; }", "'layout'"),
            ("uniform float x;\nvec4 CxStage(vec4 c) { return c; }", "'uniform'"),
            ("vec4 CxStage(vec4 c) { while (true) {} return c; }", "'while'"),
            ("vec4 CxStage(vec4 c) { sampler2D s; return c; }", "'sampler2D'"),
            ("vec4 CxStage(vec4 c) { imageStore(i, ivec2(0), c); return c; }", "'imageStore'"),
            ("vec4 CxStage(vec4 c) { return c + gl_Position; }", "'gl_Position'"),
            ("vec4 Other(vec4 c) { return c; }", "no CxStage"),
            ("/* never closed\nvec4 CxStage(vec4 c) { return c; }", "never closes"),
            ("vec4 CxStage(vec4 c) { return c; } é", "printable ASCII"),
            ("void main() {}\nvec4 CxStage(vec4 c) { return c; }", "'main'"),
        ];
        foreach ((string b, string w) in refused)
            check.Expect(PluginGlsl.Check(b) is { } why && why.Contains(w, StringComparison.Ordinal), $"a stage refused for {w} ({PluginGlsl.Check(b)})");
    }

    private static void Index(Checker check, byte[] json, byte[] stage)
    {
        ReleaseKey[] keys = ReleaseKeys.Parse(Encoding.UTF8.GetString(Res("plugins/signers")));
        byte[] text = Res("plugins/index.txt");
        PluginIndex? ix = PluginIndex.Open(text, Encoding.UTF8.GetString(Res("plugins/index.txt.sig")), keys, out string why);
        check.Expect(ix?.Signer == "fixture-plugins", $"the fixture index verifies ({why})");
        check.Expect(ix != null && ix.Vouches("vignette", "1.0.0", "plugin.json", json) && ix.Vouches("vignette", "1.0.0", "stage.glsl", stage),
                     "the index vouches for both example files");
        byte[] changed = (byte[])stage.Clone();
        changed[^2] ^= 1;
        check.Expect(ix != null && !ix.Vouches("vignette", "1.0.0", "stage.glsl", changed), "a changed byte is not vouched for");
        check.Expect(ix != null && !ix.Vouches("vignette", "1.0.1", "stage.glsl", stage), "another version is not vouched for");
        check.Expect(PluginIndex.Open(text, Encoding.UTF8.GetString(Res("plugins/index.wrongns.sig")), keys, out why) == null
                     && why.Contains("signed for 'cx-update'", StringComparison.Ordinal), $"a release-notice signature is refused ({why})");
        check.Expect(PluginIndex.Open(text, Encoding.UTF8.GetString(Res("plugins/index.txt.sig")), [], out why) == null
                     && why.Contains("no plugin signing key", StringComparison.Ordinal), $"no built-in key refuses every index ({why})");
        byte[] tampered = [.. text, .. "evil 1.0.0 stage.glsl "u8, .. Encoding.ASCII.GetBytes(new string('0', 128)), (byte)'\n'];
        check.Expect(PluginIndex.Open(tampered, Encoding.UTF8.GetString(Res("plugins/index.txt.sig")), keys, out why) == null,
                     $"a line added after signing is refused ({why})");
        check.Expect(PluginIndex.Parse("vignette 1.0.0 stage.glsl short\n", out why) == null, $"a short hash is refused ({why})");
    }

    private static void Folder(Checker check, byte[] json, byte[] stage)
    {
        ReleaseKey[] keys = ReleaseKeys.Parse(Encoding.UTF8.GetString(Res("plugins/signers")));
        string dir = Path.Combine(Path.GetTempPath(), "cx-plugin-check-" + Environment.ProcessId);
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "vignette"));
            File.WriteAllBytes(Path.Combine(dir, "vignette", "plugin.json"), json);
            File.WriteAllBytes(Path.Combine(dir, "vignette", "stage.glsl"), stage);
            File.WriteAllBytes(Path.Combine(dir, PluginLoader.IndexFile), Res("plugins/index.txt"));
            File.WriteAllBytes(Path.Combine(dir, PluginLoader.SigFile), Res("plugins/index.txt.sig"));
            var report = new List<string>();
            List<LoadedPlugin> got = PluginLoader.Load(dir, false, keys, report);
            check.Expect(got.Count == 1 && got[0].Vouch == "signed by fixture-plugins", $"the signed example loads ({string.Join("; ", report)})");
            File.AppendAllText(Path.Combine(dir, "vignette", "stage.glsl"), "\n");
            report.Clear();
            check.Expect(PluginLoader.Load(dir, false, keys, report).Count == 0 && report[^1].Contains("does not vouch", StringComparison.Ordinal),
                         $"a stage changed after signing is refused ({string.Join("; ", report)})");
            report.Clear();
            got = PluginLoader.Load(dir, true, keys, report);
            check.Expect(got.Count == 1 && got[0].Vouch.StartsWith("UNSIGNED", StringComparison.Ordinal), "developer mode loads it, marked unsigned");
            Directory.Move(Path.Combine(dir, "vignette"), Path.Combine(dir, "other"));
            report.Clear();
            check.Expect(PluginLoader.Load(dir, true, keys, report).Count == 0 && report[^1].Contains("not named after", StringComparison.Ordinal),
                         $"a folder not named after its id is refused ({string.Join("; ", report)})");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    private static void Compiles(Checker check, string body, PluginManifest? m)
    {
        if (m == null) return;
        if (Native.Sym(SymbolMap.AppManager) != IntPtr.Zero) { check.Skip("on-device: shaderc compiles nothing at boot in a game session"); return; }
        bool staged = true;
        foreach (string leaf in VulkanToolchain.Libraries) staged &= VulkanToolchain.Load(leaf);
        if (!staged) { check.Skip("the staged shaderc did not load"); return; }
        var g = new KnobGroup("plugincheck", "Graphics", "x", "On", "a throwaway", false, Effect.Live, null, null, "", register: false);
        var uniforms = m.Knobs.Select(k => new PluginUniform(k.Uniform, g.Slider(k.Key, k.Label, k.Min, k.Max, k.Step, k.Default))).ToArray();
        string? why = PostChainVk.CompileCheck(PostChain.FullscreenVs, PluginStages.Fragment(new PluginStage(m.Id, body, uniforms, g.Master!)));
        check.Expect(why == null, $"the example stage compiles to SPIR-V ({why})");
    }

    private static byte[] Res(string name)
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        if (s == null) return [];
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
