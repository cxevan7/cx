using CX.Alloy;
using CX.Framework;

namespace CX.Services.Plugins;

/// <summary>One loaded plugin's settings group, named at run time from its id: its master (off until switched on) and
/// a slider per manifest knob, each feeding the stage's uniform of the same row.</summary>
internal sealed class PluginRows
{
    /// <summary>The stage the post stack draws for the plugin, its uniforms tied to the sliders.</summary>
    public readonly PluginStage Stage;

    public PluginRows(string name, LoadedPlugin p, int order)
    {
        PluginManifest m = p.Manifest;
        var rows = KnobGroup.Declare(name, "Graphics", "Plugins", master: m.Name,
                                     masterHelp: $"{m.Name} {m.Version} ({m.License}), {p.Vouch}. A plugin from the plugins folder.",
                                     masterDefault: false, order: order, title: m.Name);
        var uniforms = new PluginUniform[m.Knobs.Length];
        for (int i = 0; i < m.Knobs.Length; i++)
        {
            PluginKnob k = m.Knobs[i];
            uniforms[i] = new PluginUniform(k.Uniform, rows.Slider(k.Key, k.Label, k.Min, k.Max, k.Step, k.Default, help: $"A setting of the {m.Name} plugin."));
        }
        Stage = new PluginStage(m.Id, p.Body, uniforms, rows.Master!);
    }
}
