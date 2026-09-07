namespace BandManager.Data.Entities;

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
    bool DefaultVisible, string? Value = null);

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
    public required List<FlyerFieldDef> Fields { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
