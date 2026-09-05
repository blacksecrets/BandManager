using System.Text.Json;
using Jint;

namespace BandManager.Data.Services;

/// <summary>
/// Extracts and parses a `const &lt;name&gt; = [...]` array literal out of
/// one of the Band's own site JS files (calendar.js/media.js/gallery.js) -
/// ported from the identical hand-rolled bracket/quote-aware scanner
/// repeated in the old app's gigsSource.js/mediaSource.js/gallerySource.js.
///
/// These files aren't guaranteed to be valid JSON (a human may have hand-
/// edited one directly on GitHub, with JS-only syntax like comments or
/// unquoted keys) - the old app deliberately used JS's own eval/new
/// Function for that tolerance instead of JSON.parse, so this uses Jint
/// (a real, embedded JS interpreter) the same way: evaluate the literal
/// for real, then let the *engine's own* JSON.stringify hand back
/// something System.Text.Json can parse - avoids depending on Jint's
/// object-model API surface for traversal, just Evaluate().AsString().
/// </summary>
public static class SiteJsArrayParser
{
    public static string? ExtractArrayLiteral(string code, string constName)
    {
        var marker = $"const {constName} = ";
        var start = code.IndexOf(marker, StringComparison.Ordinal);
        if (start == -1) return null;

        var i = start + marker.Length;
        if (i >= code.Length || code[i] != '[') return null;

        var depth = 0;
        char? quote = null;
        var arrayStart = i;
        for (; i < code.Length; i++)
        {
            var ch = code[i];
            var prev = i > 0 ? code[i - 1] : '\0';
            if (quote is not null)
            {
                if (ch == quote && prev != '\\') quote = null;
                continue;
            }
            if (ch is '"' or '\'' or '`') { quote = ch; continue; }
            if (ch == '[') depth++;
            if (ch == ']')
            {
                depth--;
                if (depth == 0) return code[arrayStart..(i + 1)];
            }
        }
        return null;
    }

    public static List<T> ParseArray<T>(string code, string constName)
    {
        var literal = ExtractArrayLiteral(code, constName);
        if (literal is null) return [];

        using var engine = new Engine(options => options.LimitMemory(64_000_000).TimeoutInterval(TimeSpan.FromSeconds(5)));
        var jsonString = engine.Evaluate($"JSON.stringify({literal})").AsString();
        var result = JsonSerializer.Deserialize<List<T>>(jsonString, JsonOpts);
        return result ?? [];
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
}
