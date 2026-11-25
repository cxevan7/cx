using CX.Services.Update;

namespace CX.Services.Plugins;

/// <summary>One file a signed index vouches for: the plugin, its version, the file's name in its folder, its SHA-512.</summary>
internal readonly record struct PluginIndexEntry(string Id, string Version, string File, string Sha512);

/// <summary>The plugin index: <c>index.txt</c> beside the plugins, one line per file
/// (<c>id version file sha512hex</c>), signed by a CX plugin key under <see cref="Namespace"/> in <c>index.txt.sig</c>.</summary>
internal sealed class PluginIndex
{
    /// <summary>The namespace plugin indexes are signed under: a release notice's signature does not verify an index.</summary>
    public const string Namespace = "cx-plugins";
    /// <summary>The most an index may weigh, in bytes.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>The key that signed the index.</summary>
    public string Signer { get; }
    private readonly PluginIndexEntry[] _entries;

    private PluginIndex(string signer, PluginIndexEntry[] entries) { Signer = signer; _entries = entries; }

    /// <summary>The index in <paramref name="text"/> when <paramref name="sig"/> verifies it against <paramref name="keys"/>;
    /// null with <paramref name="why"/> otherwise. A malformed line refuses the whole index.</summary>
    public static PluginIndex? Open(ReadOnlySpan<byte> text, string sig, ReleaseKey[] keys, out string why)
    {
        if (text.Length > MaxBytes) { why = $"the index is over {MaxBytes} bytes"; return null; }
        if (keys.Length == 0) { why = "this build carries no plugin signing key"; return null; }
        if (SshSig.Verify(text, sig, keys, Namespace, out why) is not { } signer) return null;
        if (Parse(System.Text.Encoding.UTF8.GetString(text), out why) is not { } entries) return null;
        return new PluginIndex(signer, entries);
    }

    /// <summary>Each entry line of <paramref name="text"/>; blank lines and <c>#</c> comments are skipped.</summary>
    internal static PluginIndexEntry[]? Parse(string text, out string why)
    {
        var list = new List<PluginIndexEntry>();
        int n = 0;
        foreach (string raw in text.Split('\n'))
        {
            n++;
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            string[] f = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (f.Length != 4 || f[3].Length != 128 || !Hex(f[3])) { why = $"index line {n} is not 'id version file sha512'"; return null; }
            list.Add(new PluginIndexEntry(f[0], f[1], f[2], f[3]));
        }
        why = "";
        return [.. list];
    }

    /// <summary>Does the index vouch for <paramref name="file"/> of plugin <paramref name="id"/> at <paramref name="version"/>
    /// with exactly <paramref name="bytes"/>?</summary>
    public bool Vouches(string id, string version, string file, ReadOnlySpan<byte> bytes)
    {
        string hex = Convert.ToHexStringLower(SshSig.Sha512(bytes));
        foreach (PluginIndexEntry e in _entries)
            if (e.Id == id && e.Version == version && e.File == file) return e.Sha512 == hex;
        return false;
    }

    private static bool Hex(string s)
    {
        foreach (char c in s) if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f'))) return false;
        return true;
    }
}
