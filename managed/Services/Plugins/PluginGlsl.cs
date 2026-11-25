namespace CX.Services.Plugins;

/// <summary>The rules a plugin stage's GLSL body answers to before it reaches the compiler: plain text that defines
/// <c>vec4 CxStage(vec4 c)</c> and declares nothing the stack has to bind. It reads <c>uSrc</c>, <c>uDepth</c>,
/// <c>vUV</c> and its own knob uniforms, which the loader declares; it cannot name a buffer, an image or the
/// preprocessor, so it reaches no memory past the frame it is handed.</summary>
internal static class PluginGlsl
{
    /// <summary>The most a stage body may weigh, in bytes.</summary>
    public const int MaxBytes = 32 * 1024;
    /// <summary>The function every body defines: the stack's main calls it.</summary>
    public const string Entry = "CxStage";

    /// <summary>Words a body may not use: declarations the stack would have to bind, a second entry point, and the
    /// loops whose bound is not written into the loop itself.</summary>
    private static readonly string[] Refused =
        ["layout", "uniform", "buffer", "shared", "attribute", "varying", "subroutine", "main", "while", "do", "frag", "uDither"];

    /// <summary>Why <paramref name="body"/> is refused, or null when it may be compiled.
    /// ponytail: a for loop with a huge bound can still stall the GPU; a device-lost quarantine is the upgrade.</summary>
    public static string? Check(string body)
    {
        if (body.Length == 0 || body.Length > MaxBytes) return $"the stage is empty or over {MaxBytes} bytes";
        foreach (char c in body)
            if (c is not ('\n' or '\r' or '\t') && c is < ' ' or > '~') return "the stage holds a character that is not printable ASCII";
        string code = StripComments(body, out string? open);
        if (open != null) return open;
        if (code.Contains('#')) return "the stage uses the preprocessor (#)";
        if (code.Contains('\\')) return "the stage uses a line continuation (\\)";
        bool entry = false;
        int i = 0;
        while (i < code.Length)
        {
            if (!(char.IsAsciiLetter(code[i]) || code[i] == '_')) { i++; continue; }
            int start = i;
            while (i < code.Length && (char.IsAsciiLetterOrDigit(code[i]) || code[i] == '_')) i++;
            string w = code[start..i];
            if (Array.IndexOf(Refused, w) >= 0) return $"the stage uses '{w}'";
            if (w.StartsWith("sampler", StringComparison.Ordinal) || w.StartsWith("image", StringComparison.Ordinal))
                return $"the stage declares a '{w}'";
            if (w.StartsWith("gl_", StringComparison.Ordinal) && w != "gl_FragCoord") return $"the stage uses '{w}'";
            entry |= w == Entry;
        }
        return entry ? null : $"the stage defines no {Entry}";
    }

    /// <summary><paramref name="s"/> with its comments blanked to spaces; <paramref name="open"/> says why when a block
    /// comment never closes.</summary>
    private static string StripComments(string s, out string? open)
    {
        var o = new System.Text.StringBuilder(s.Length);
        open = null;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                o.Append('\n');
            }
            else if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0) { open = "the stage opens a comment it never closes"; return ""; }
                o.Append(' ');
                i = end + 1;
            }
            else o.Append(s[i]);
        }
        return o.ToString();
    }
}
