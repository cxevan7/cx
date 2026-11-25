using System.Reflection;
using CX.Alloy;
using CX.Framework;
using CX.Services.Update;

namespace CX.Services.Plugins;

/// <summary>Loads the plugins folder once at start: each plugin that passes <see cref="PluginLoader"/> gets a settings
/// group of its own (off until the player switches it on) and its stage in <see cref="PluginStages"/>.</summary>
public sealed class PluginService : IExtenderService
{
    /// <summary>After the services whose groups declare at their own start, so a plugin group never takes their place.</summary>
    public int InitOrder => 100;

    private sealed class PluginReport { public readonly List<string> Lines = []; }
    private static readonly PluginReport _report = new();

    public void Initialize(ExtenderLog log, HookService hooks)
    {
        var plugins = KnobGroup.Declare("plugins", "Graphics", "Plugins", master: "Plugins",
                                  masterHelp: "Loads the visual plugins in the plugins folder beside cx.cfg. Each one still starts off "
                                            + "and has its own switch below. Takes effect at the next start.",
                                  masterEffect: Effect.Restart, order: 900, title: "Plugins");
        Knob<bool> developer = plugins.Toggle("developer", "Load unsigned plugins", false, Tier.Advanced, Effect.Restart,
                                        help: "For plugin authors: loads plugins the signed index does not vouch for. Anything in "
                                            + "the plugins folder then runs on your graphics card unchecked.");
        plugins.Status = static () => string.Join("; ", _report.Lines);
        plugins.PanelStatus = static () => string.Join(Knob.NoteBreak, _report.Lines);
        if (!plugins.Master!.Boot) { _report.Lines.Add("plugins are off"); return; }
        string dir = Path.Combine(Path.GetDirectoryName(Settings.Path) ?? ".", PluginLoader.Folder);
        List<LoadedPlugin> loaded = PluginLoader.Load(dir, developer.Boot, ReleaseKeys.Parse(Signers()), _report.Lines);
        int order = 901;
        foreach (LoadedPlugin p in loaded)
        {
            if (KnobGroup.All.Any(x => x.Name == GroupName(p.Manifest.Id)))
            { _report.Lines.Add($"{p.Manifest.Id}: refused -- its settings name is taken"); continue; }
            PluginStage stage = RowsFor(p, order++);
            if (!PluginStages.Add(stage)) { _report.Lines.Add($"{p.Manifest.Id}: refused -- only {PluginStages.Max} plugins draw"); break; }
        }
        foreach (string line in _report.Lines) log.Always("PluginService: " + line);
    }

    /// <summary>The settings group a plugin's knobs live under: its id behind a prefix no built-in group uses.</summary>
    internal static string GroupName(string id) => "plugin_" + id;

    private static PluginStage RowsFor(LoadedPlugin p, int order) => new PluginRows(GroupName(p.Manifest.Id), p, order).Stage;

    /// <summary>The plugin signing keys built into this extender: <c>packaging/plugin_signers</c>, embedded.</summary>
    private static string Signers()
    {
        using Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream("plugin_signers");
        if (s == null) return "";
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }
}
