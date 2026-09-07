using System.Text;
using System.Text.RegularExpressions;

namespace BandManager.Data.Services;

public record TextBlock(int Start, int End, string Text);

/// <summary>
/// Shared regex/quote-aware text-editing primitives behind
/// GigsSiteEditor/MediaSiteEditor/GallerySiteEditor - ported from the old
/// app's siteEditor.js/mediaEditor.js/galleryEditor.js, which all hand-
/// rolled the same block-finding/insertion logic three times. Edits a
/// site's hand-authored JS array file (calendar.js/media.js/gallery.js)
/// by finding and replacing just one {...} block's text, leaving
/// everything else - formatting, other entries, comments - byte-for-byte
/// untouched, rather than parsing+re-serializing as JSON (which would
/// lose both).
/// </summary>
public static class SiteTextEditing
{
    public static string EscapeForQuotes(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>Finds the bounds of one entry's {...} object within the
    /// array's source text, located by matching `id: "..."` (or a
    /// fallback field like `title`/`alt` for legacy entries with no id) -
    /// quote-aware bracket matching so a `}` inside a string value never
    /// miscounts as the block's real closing brace.</summary>
    public static TextBlock? FindBlock(string code, string field, string value)
    {
        var pattern = new Regex($@"\b{Regex.Escape(field)}\s*:\s*""{Regex.Escape(value)}""");
        var match = pattern.Match(code);
        if (!match.Success) return null;

        var start = match.Index;
        var depth = 0;
        while (start > 0)
        {
            start--;
            if (code[start] == '}') depth++;
            else if (code[start] == '{')
            {
                if (depth == 0) break;
                depth--;
            }
        }

        var i = start;
        char? quote = null;
        var braceDepth = 0;
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
            if (ch == '{') braceDepth++;
            if (ch == '}')
            {
                braceDepth--;
                if (braceDepth == 0) { i++; break; }
            }
        }

        return new TextBlock(start, i, code[start..i]);
    }

    /// <summary>Finds the [start,end) bounds of the array literal assigned
    /// to `const {arrayName} = [...]`.</summary>
    public static (int Start, int End)? FindArrayBounds(string code, string arrayName)
    {
        var marker = $"const {arrayName} = ";
        var start = code.IndexOf(marker, StringComparison.Ordinal);
        if (start == -1) return null;
        var i = start + marker.Length;
        if (i >= code.Length || code[i] != '[') return null;
        var arrayStart = i;
        var depth = 0;
        char? quote = null;
        for (; i < code.Length; i++)
        {
            var ch = code[i];
            var prev = i > 0 ? code[i - 1] : '\0';
            if (quote is not null) { if (ch == quote && prev != '\\') quote = null; continue; }
            if (ch is '"' or '\'' or '`') { quote = ch; continue; }
            if (ch == '[') depth++;
            if (ch == ']') { depth--; if (depth == 0) return (arrayStart, i + 1); }
        }
        return null;
    }

    /// <summary>Replaces one field's quoted string value within a block's
    /// text. Leaves the block unchanged if that field isn't present -
    /// never adds a field that isn't there (see AppendField for the
    /// handful of fields allowed to be added).</summary>
    public static string SetField(string blockText, string field, string value)
    {
        var pattern = new Regex($@"(\b{Regex.Escape(field)}\s*:\s*)""(?:[^""\\]|\\.)*""");
        if (!pattern.IsMatch(blockText)) return blockText;
        return pattern.Replace(blockText, $"$1\"{EscapeForQuotes(value)}\"", 1);
    }

    /// <summary>Finds the [start,end) bounds of a field's array-literal
    /// VALUE (e.g. `with: [ {...}, {...} ]`) within a block's text - same
    /// bracket/quote-depth scanning as FindArrayBounds, but anchored on
    /// `field: [` instead of a top-level `const name = `. Returns null if
    /// the field isn't present, or its current value isn't an array
    /// literal (e.g. still the old scalar string form) - SetArrayField
    /// below falls back to AppendField in that case, same "never guess at
    /// structure that isn't there" rule as SetField.</summary>
    public static (int Start, int End)? FindFieldArrayBounds(string blockText, string field)
    {
        var marker = new Regex($@"\b{Regex.Escape(field)}\s*:\s*\[");
        var m = marker.Match(blockText);
        if (!m.Success) return null;

        var i = m.Index + m.Length - 1; // position of the '['
        var arrayStart = i;
        var depth = 0;
        char? quote = null;
        for (; i < blockText.Length; i++)
        {
            var ch = blockText[i];
            var prev = i > 0 ? blockText[i - 1] : '\0';
            if (quote is not null) { if (ch == quote && prev != '\\') quote = null; continue; }
            if (ch is '"' or '\'' or '`') { quote = ch; continue; }
            if (ch == '[') depth++;
            if (ch == ']') { depth--; if (depth == 0) return (arrayStart, i + 1); }
        }
        return null;
    }

    /// <summary>Replaces a field's array-literal value in place if it
    /// already exists as an array (FindFieldArrayBounds succeeds); returns
    /// the block unchanged otherwise - the caller (GigsSiteEditor) falls
    /// back to AppendField (shape-agnostic - takes raw JS text) for
    /// first-time creation, same two-step pattern SetBooleanField already
    /// uses for scalars.</summary>
    public static string SetArrayField(string blockText, string field, string rawArrayLiteral)
    {
        var bounds = FindFieldArrayBounds(blockText, field);
        if (bounds is null) return blockText;
        return blockText[..bounds.Value.Start] + rawArrayLiteral + blockText[bounds.Value.End..];
    }

    public static bool HasField(string blockText, string field) =>
        Regex.IsMatch(blockText, $@"\b{Regex.Escape(field)}\s*:");

    /// <summary>Appends a new field as the block's new last property,
    /// right before the closing `}` - walks back past any trailing blank/
    /// comment-only lines first, so a new field's separating comma never
    /// lands inside a trailing documentation comment where the parser
    /// never sees it.</summary>
    public static string AppendField(string blockText, string field, string rawValue)
    {
        var closeMatch = Regex.Match(blockText, @"(\r?\n)(\s*)\}\s*$");
        if (!closeMatch.Success) return blockText; // unexpected shape - leave untouched rather than risk corrupting it

        var cut = closeMatch.Index;
        for (; ; )
        {
            var nlIdx = blockText.LastIndexOf('\n', cut - 1);
            if (nlIdx < 0) break;
            var lineStart = nlIdx + 1;
            var line = blockText[lineStart..cut];
            var trimmed = line.Trim();
            if (trimmed.Length != 0 && !trimmed.StartsWith("//")) break;
            cut = nlIdx > 0 && blockText[nlIdx - 1] == '\r' ? nlIdx - 1 : nlIdx;
        }

        var before = blockText[..cut];
        var trailing = blockText[cut..closeMatch.Index];

        var indentMatch = Regex.Match(blockText, @"\n(\s+)\S+\s*:");
        var fieldIndent = indentMatch.Success ? indentMatch.Groups[1].Value : "        ";
        var eol = blockText.Contains("\r\n") ? "\r\n" : "\n";
        var needsComma = !Regex.IsMatch(before, @",\s*$");
        var insertion = $"{(needsComma ? "," : "")}{eol}{fieldIndent}{field}: {rawValue}";
        return before + insertion + trailing + blockText[closeMatch.Index..];
    }

    public static string SetBooleanField(string blockText, string field, bool value)
    {
        var pattern = new Regex($@"\b{Regex.Escape(field)}\s*:\s*(?:true|false)\b");
        var rawValue = value ? "true" : "false";
        return pattern.IsMatch(blockText)
            ? pattern.Replace(blockText, $"{field}: {rawValue}", 1)
            : AppendField(blockText, field, rawValue);
    }

    /// <summary>Inserts `insertionBody` as new content right before a
    /// trailing `]` (or `}`) in `text`, walking back past any trailing
    /// blank/comment-only lines first - shared by whole-entry inserts
    /// into an array.</summary>
    public static string? InsertBeforeClose(string text, char closeChar, string insertionBody)
    {
        var closeMatch = Regex.Match(text, $@"(\r?\n)(\s*)\{closeChar}\s*$");
        if (!closeMatch.Success) return null;

        var cut = closeMatch.Index;
        for (; ; )
        {
            var nlIdx = text.LastIndexOf('\n', cut - 1);
            if (nlIdx < 0) break;
            var lineStart = nlIdx + 1;
            var line = text[lineStart..cut];
            var trimmed = line.Trim();
            if (trimmed.Length != 0 && !trimmed.StartsWith("//")) break;
            cut = nlIdx > 0 && text[nlIdx - 1] == '\r' ? nlIdx - 1 : nlIdx;
        }

        var before = text[..cut];
        var trailing = text[cut..closeMatch.Index];
        var eol = text.Contains("\r\n") ? "\r\n" : "\n";
        var needsComma = !Regex.IsMatch(before, @",\s*$");
        var insertion = $"{(needsComma ? "," : "")}{eol}{insertionBody}";
        return before + insertion + trailing + text[closeMatch.Index..];
    }

    public static string Slugify(string text, string fallback)
    {
        var slug = Regex.Replace(text.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 60) slug = slug[..60];
        return slug.Length == 0 ? fallback : slug;
    }

    /// <summary>Finds every `id: "..."` value currently in the file and
    /// returns baseSlug, or baseSlug-2/-3/... if it's already taken.</summary>
    public static string UniqueId(string code, string baseSlug)
    {
        var existing = new HashSet<string>();
        foreach (Match m in Regex.Matches(code, @"\bid\s*:\s*""([^""]*)"""))
        {
            existing.Add(m.Groups[1].Value);
        }
        if (!existing.Contains(baseSlug)) return baseSlug;
        var n = 2;
        while (existing.Contains($"{baseSlug}-{n}")) n++;
        return $"{baseSlug}-{n}";
    }

    /// <summary>Removes a whole block (start..end), including whichever
    /// neighboring comma keeps the array/object valid.</summary>
    public static string RemoveBlock(string content, TextBlock block)
    {
        var start = block.Start;
        var end = block.End;
        var afterMatch = Regex.Match(content[end..Math.Min(content.Length, end + 200)], @"^\s*,\s*");
        if (afterMatch.Success)
        {
            end += afterMatch.Length;
        }
        else
        {
            var beforeText = content[Math.Max(0, start - 200)..start];
            var beforeMatch = Regex.Match(beforeText, @",\s*$");
            if (beforeMatch.Success) start -= beforeMatch.Length;
        }
        return content[..start] + content[end..];
    }
}
