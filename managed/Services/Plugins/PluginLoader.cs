using CX.Services.Update;

namespace CX.Services.Plugins;

/// <summary>A plugin that passed every check: its manifest, its stage body and who vouched for it.</summary>
internal sealed record LoadedPlugin(PluginManifest Manifest, string Body, string Vouch);

/// <summary>Reads the plugins folder: one subfolder per plugin, each a <c>plugin.json</c> and the stage it names. A
/// plugin loads only when the signed index vouches for both files byte for byte, or in developer mode when it is
/// unsigned; anything else is refused with its reason, and nothing it holds is compiled.</summary>
internal static class PluginLoader
{
    /// <summary>The folder beside <c>cx.cfg</c> that holds the plugins and the index.</summary>
    public const string Folder = "plugins";
    public const string ManifestFile = "plugin.json", IndexFile = "index.txt", SigFile = "index.txt.sig";
    /// <summary>The most subfolders one start looks at.</summary>
    public const int MaxFolders = 32;

    /// <summary>The plugins under <paramref name="dir"/> that load, in folder-name order, and a line per folder saying
    /// what became of it.</summary>
    public static List<LoadedPlugin> Load(string dir, bool developer, ReleaseKey[] keys, List<string> report)
    {
        var loaded = new List<LoadedPlugin>();
        if (!Directory.Exists(dir)) { report.Add($"no {Folder} folder"); return loaded; }
        PluginIndex? index = null;
        string indexWhy = "there is no signed index";
        if (Read(Path.Combine(dir, IndexFile), PluginIndex.MaxBytes) is { } text && Read(Path.Combine(dir, SigFile), 64 * 1024) is { } sig)
            index = PluginIndex.Open(text, System.Text.Encoding.UTF8.GetString(sig), keys, out indexWhy);
        report.Add(index != null ? $"the index is signed by {index.Signer}" : $"the index does not verify: {indexWhy}");
        string[] folders = Directory.GetDirectories(dir);
        Array.Sort(folders, StringComparer.Ordinal);
        for (int i = 0; i < folders.Length && i < MaxFolders; i++)
        {
            string name = Path.GetFileName(folders[i]);
            LoadedPlugin? p = One(folders[i], name, index, developer, out string why);
            report.Add(p != null ? $"{name} {p.Manifest.Version}: loaded, {p.Vouch}" : $"{name}: refused -- {why}");
            if (p != null) loaded.Add(p);
        }
        if (folders.Length > MaxFolders) report.Add($"only the first {MaxFolders} folders were read");
        return loaded;
    }

    private static LoadedPlugin? One(string folder, string name, PluginIndex? index, bool developer, out string why)
    {
        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) { why = "the folder is a link"; return null; }
        if (Read(Path.Combine(folder, ManifestFile), PluginManifest.MaxBytes) is not { } json) { why = $"no readable {ManifestFile}"; return null; }
        if (PluginManifest.Parse(json, out why) is not { } m) return null;
        if (m.Id != name) { why = $"the folder is not named after the id '{m.Id}'"; return null; }
        if (Read(Path.Combine(folder, m.Stage), PluginGlsl.MaxBytes) is not { } stage) { why = $"no readable {m.Stage}"; return null; }
        string vouch;
        if (index != null && index.Vouches(m.Id, m.Version, ManifestFile, json) && index.Vouches(m.Id, m.Version, m.Stage, stage))
            vouch = "signed by " + index.Signer;
        else if (developer) vouch = "UNSIGNED (developer mode)";
        else { why = "the signed index does not vouch for these files"; return null; }
        string body = System.Text.Encoding.ASCII.GetString(stage);
        if (PluginGlsl.Check(body) is { } bad) { why = bad; return null; }
        why = "";
        return new LoadedPlugin(m, body, vouch);
    }

    /// <summary>The whole of <paramref name="path"/> when it is a plain file of at most <paramref name="max"/> bytes, else null.</summary>
    private static byte[]? Read(string path, int max)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || (fi.Attributes & FileAttributes.ReparsePoint) != 0 || fi.Length > max) return null;
            return File.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
