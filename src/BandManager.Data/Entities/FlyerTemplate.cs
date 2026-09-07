using System.Text.Json.Serialization;

namespace BandManager.Data.Entities;

// String, not the default int - settings.js/flyerEditor.js/
// flyerTemplateEditor.js all compare field.type against 'Text'/'Image'
// string literals. Without this, every field (Text or Image) silently
// fails BOTH comparisons and falls into whichever branch the code treats
// as "not Text" - in the Template/Flyer editors that meant no field ever
// showed its font/color/style controls, and saving an edit threw a
// JSON-binding error server-side (a raw number can't bind to the DTO's
// string Type property) - caught live while building the Bold/Italic/
// Underline feature, not by design.
[JsonConverter(typeof(JsonStringEnumConverter<FlyerFieldType>))]
public enum FlyerFieldType { Text, Image }

/// <summary>One positioned field, shared by both a FlyerTemplate's default
/// layout and a generated Flyer's actual values - distinguished by whether
/// Value is set. X/Y/FontSize are stored as FRACTIONS of the background
/// image's own width/height (0.0-1.0), not raw pixels, so the client-side
/// live preview and the server-side SkiaSharp render (FlyerRenderer) agree
/// exactly regardless of what size the browser happens to display the
/// image at. Value holds typed text for a Text field, or the picked logo's
/// CatalogItem id (as a string) for an Image field.</summary>
public record FlyerFieldDef(
    string Key, string Label, FlyerFieldType Type,
    double X, double Y, double? FontSize, string? FontFamily, string? Color,
    bool DefaultVisible, string? Value = null,
    // Synthetic styling applied at render time (FlyerRenderer), not tied
    // to the chosen font file having its own bold/italic variant - most of
    // the 7 bundled fonts are single-weight display faces with no such
    // variant to switch to. Text fields only; meaningless for Image.
    bool Bold = false, bool Italic = false, bool Underline = false);

/// <summary>A reusable flyer background + its default field layout - see
/// FlyerFieldDef. BackgroundCatalogItem is Restrict-deleted: a template
/// with a missing background is unusable, so CatalogController's
/// reclassify/delete paths must check for this and reject with a clear
/// error rather than let the FK throw.</summary>
public class FlyerTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public Guid BackgroundCatalogItemId { get; set; }
    public CatalogItem BackgroundCatalogItem { get; set; } = null!;

    public required string Name { get; set; }

    // The template-level default a field's own FontFamily starts from -
    // set once here rather than needing every field individually chosen,
    // per the original "each field has its own font, but there should be
    // one declared at the highest level" request. Purely a starting point/
    // bulk-apply convenience (see flyerTemplateEditor.js's "Apply to all
    // fields" button) - once a field is created its FontFamily is a
    // concrete value, not a live reference back to this, so changing this
    // later doesn't retroactively move already-placed fields.
    public string? DefaultFontFamily { get; set; }

    public required List<FlyerFieldDef> Fields { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
