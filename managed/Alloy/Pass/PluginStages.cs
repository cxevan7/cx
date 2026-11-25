using System.Text;
using CX.Framework;

namespace CX.Alloy;

/// <summary>One plugin's display-space stage: its id, its GLSL body (a <c>vec4 CxStage(vec4 c)</c>), the float
/// uniforms its knobs feed, and its on switch.</summary>
internal sealed record PluginStage(string Id, string Body, PluginUniform[] Uniforms, Knob<bool> On);

/// <summary>A plugin stage's float uniform and the knob that sets it each frame.</summary>
internal readonly record struct PluginUniform(string Name, Knob<float> Value);

/// <summary>The plugin stages the post stack draws, in load order, after the built-in grading stages and before the
/// anti-aliaser. The plugin service fills it at startup; the stack reads it every frame.</summary>
internal static class PluginStages
{
    /// <summary>The most plugin stages one session draws: the stack's stage mask is 32 bits wide.</summary>
    internal const int Max = 8;

    private sealed class PluginStageState { public PluginStage[] All = []; }
    private static readonly PluginStageState _s = new();

    /// <summary>Every registered stage; the array is replaced whole, never changed in place.</summary>
    internal static PluginStage[] All => Volatile.Read(ref _s.All);

    /// <summary>Register <paramref name="p"/>; false once <see cref="Max"/> stages are registered.</summary>
    internal static bool Add(PluginStage p)
    {
        lock (_s)
        {
            if (_s.All.Length >= Max) return false;
            Volatile.Write(ref _s.All, [.. _s.All, p]);
            return true;
        }
    }

    /// <summary>The stage's fragment: the stack's head, a declaration per uniform, the plugin's body, the stack's main.</summary>
    internal static string Fragment(PluginStage p)
    {
        var sb = new StringBuilder(PostStack.PostStageHead);
        sb.Append('\n');
        foreach (PluginUniform u in p.Uniforms) sb.Append("uniform float ").Append(u.Name).Append(";\n");
        sb.Append(p.Body).Append('\n').Append(PostStack.PostStageMain);
        return sb.ToString();
    }
}
