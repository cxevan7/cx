using System.Text.Json;

namespace CX.Services.Plugins;

/// <summary>One plugin knob a manifest declares: a float slider feeding the stage's uniform of the same row.</summary>
internal sealed record PluginKnob(string Key, string Label, float Min, float Max, float Step, float Default, string Uniform);

/// <summary>A parsed <c>plugin.json</c>. Strict: an unknown field, a value out of its bounds or a missing one refuses
/// the whole manifest, so a plugin is either exactly what its manifest says or not loaded.</summary>
internal sealed record PluginManifest(string Id, string Version, string Name, string License, string Stage, PluginKnob[] Knobs)
{
    /// <summary>The only kind this loader takes: a display-space stage of the post stack.</summary>
    public const string KindShader = "shader";
    /// <summary>The most a manifest may weigh, in bytes.</summary>
    public const int MaxBytes = 16 * 1024;
    /// <summary>The most knobs one plugin may declare.</summary>
    public const int MaxKnobs = 8;
    private const int MaxText = 48;

    private static readonly string[] Fields = ["id", "version", "kind", "name", "license", "stage", "knobs"];
    private static readonly string[] KnobFields = ["key", "label", "min", "max", "step", "default", "uniform"];
    /// <summary>Uniforms the stack's own head and main declare: a knob may not take one.</summary>
    private static readonly string[] Reserved = ["uSrc", "uDepth", "uDither"];

    /// <summary>The manifest in <paramref name="json"/>, or null with <paramref name="why"/> naming what it refused.</summary>
    public static PluginManifest? Parse(ReadOnlySpan<byte> json, out string why)
    {
        if (json.Length > MaxBytes) { why = $"plugin.json is over {MaxBytes} bytes"; return null; }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions { MaxDepth = 4 });
            return Read(doc.RootElement, out why);
        }
        catch (JsonException e) { why = "plugin.json is not JSON: " + e.Message; return null; }
    }

    private static PluginManifest? Read(JsonElement root, out string why)
    {
        if (root.ValueKind != JsonValueKind.Object) { why = "plugin.json is not an object"; return null; }
        if (Unknown(root, Fields) is { } extra) { why = $"unknown field '{extra}'"; return null; }
        string? id = Str(root, "id"), version = Str(root, "version"), kind = Str(root, "kind");
        string? name = Str(root, "name"), license = Str(root, "license"), stage = Str(root, "stage");
        if (id == null || !Word(id, 2, 24, digits: true)) { why = "id must be 2 to 24 lowercase letters and digits, starting with a letter"; return null; }
        if (version == null || !VersionText(version)) { why = "version must be major.minor.patch"; return null; }
        if (kind != KindShader) { why = $"kind must be '{KindShader}'"; return null; }
        if (name == null || !Plain(name)) { why = $"name must be 1 to {MaxText} printable characters"; return null; }
        if (license == null || !Plain(license)) { why = $"license must be 1 to {MaxText} printable characters"; return null; }
        if (stage == null || !FileName(stage)) { why = "stage must name a .glsl file in the plugin's own folder"; return null; }
        var knobs = new List<PluginKnob>();
        if (root.TryGetProperty("knobs", out JsonElement arr))
        {
            if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() > MaxKnobs) { why = $"knobs must be a list of at most {MaxKnobs}"; return null; }
            foreach (JsonElement k in arr.EnumerateArray())
            {
                if (Knob(k, knobs, out why) is not { } knob) return null;
                knobs.Add(knob);
            }
        }
        why = "";
        return new PluginManifest(id, version, name, license, stage, [.. knobs]);
    }

    private static PluginKnob? Knob(JsonElement k, List<PluginKnob> had, out string why)
    {
        if (k.ValueKind != JsonValueKind.Object) { why = "a knob is not an object"; return null; }
        if (Unknown(k, KnobFields) is { } extra) { why = $"unknown knob field '{extra}'"; return null; }
        string? key = Str(k, "key"), label = Str(k, "label"), uniform = Str(k, "uniform");
        if (key == null || !Word(key, 1, 16, digits: true) || key == "enabled") { why = "a knob key must be 1 to 16 lowercase letters and digits, not 'enabled'"; return null; }
        if (label == null || !Plain(label)) { why = $"knob {key}'s label must be 1 to {MaxText} printable characters"; return null; }
        if (uniform == null || !UniformName(uniform) || Array.IndexOf(Reserved, uniform) >= 0)
        { why = $"knob {key}'s uniform must be u, a capital, then up to 30 letters and digits, and not the stack's own"; return null; }
        if (Num(k, "min") is not { } min || Num(k, "max") is not { } max || Num(k, "step") is not { } step || Num(k, "default") is not { } def)
        { why = $"knob {key} needs finite min, max, step and default"; return null; }
        if (!(min < max) || !(step > 0f) || def < min || def > max) { why = $"knob {key}'s range is not min < max, step > 0, min <= default <= max"; return null; }
        foreach (PluginKnob o in had)
            if (o.Key == key || o.Uniform == uniform) { why = $"knob {key} repeats a key or a uniform"; return null; }
        why = "";
        return new PluginKnob(key, label, min, max, step, def, uniform);
    }

    private static string? Unknown(JsonElement o, string[] allowed)
    {
        foreach (JsonProperty p in o.EnumerateObject())
            if (Array.IndexOf(allowed, p.Name) < 0) return p.Name;
        return null;
    }

    private static string? Str(JsonElement o, string name)
        => o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static float? Num(JsonElement o, string name)
        => o.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out float f) && float.IsFinite(f) ? f : null;

    /// <summary>Lowercase ASCII letters (and digits when allowed), a letter first, within the lengths given.</summary>
    internal static bool Word(string s, int min, int max, bool digits)
    {
        if (s.Length < min || s.Length > max || s[0] is < 'a' or > 'z') return false;
        foreach (char c in s) if (c is not (>= 'a' and <= 'z') && !(digits && c is >= '0' and <= '9')) return false;
        return true;
    }

    private static bool VersionText(string s)
    {
        string[] parts = s.Split('.');
        if (parts.Length != 3) return false;
        foreach (string p in parts)
        {
            if (p.Length is 0 or > 5) return false;
            foreach (char c in p) if (c is < '0' or > '9') return false;
        }
        return true;
    }

    private static bool Plain(string s)
    {
        if (s.Length is 0 or > MaxText) return false;
        foreach (char c in s) if (c is < ' ' or > '~') return false;
        return true;
    }

    /// <summary>A bare <c>.glsl</c> file name: no folder, no dot-dot, lowercase letters, digits and underscores.</summary>
    internal static bool FileName(string s)
        => s.EndsWith(".glsl", StringComparison.Ordinal) && s.Length > 5 && Word(s[..^5].Replace('_', 'a'), 1, 32, digits: true);

    private static bool UniformName(string s)
    {
        if (s.Length is < 2 or > 32 || s[0] != 'u' || s[1] is < 'A' or > 'Z') return false;
        foreach (char c in s) if (!char.IsAsciiLetterOrDigit(c)) return false;
        return true;
    }
}
