using System.Text.Json.Serialization;

namespace BandManager.Data.Entities;

// String, not the default int - settings.js/flyerEditor.js compare
// field.type against 'Text'/'Image' string literals. Without this, every
// field (Text or Image) silently fails BOTH comparisons and falls into
// whichever branch the code treats as "not Text" - caught live while
// building the Bold/Italic/Underline feature, not by design.
[JsonConverter(typeof(JsonStringEnumConverter<FlyerFieldType>))]
public enum FlyerFieldType { Text, Image }

/// <summary>One positioned field on a generated Flyer. X/Y/FontSize are
/// stored as FRACTIONS of the background image's own width/height
/// (0.0-1.0), not raw pixels, so the client-side live preview and the
/// server-side SkiaSharp render (FlyerRenderer) agree exactly regardless
/// of what size the browser happens to display the image at. Value holds
/// typed text for a Text field, or the picked logo's CatalogItem id (as a
/// string) for an Image field.
///
/// FontSize doubles as the "resize" control for both field types (font
/// size for Text, target height for a logo's Image, width auto-derived
/// from its own aspect ratio) - there's no separate Width/Height, since
/// nothing here wraps text or needs an independent box.</summary>
public record FlyerFieldDef(
    string Key, string Label, FlyerFieldType Type,
    double X, double Y, double? FontSize, string? FontFamily, string? Color,
    bool DefaultVisible, string? Value = null,
    // Synthetic styling applied at render time (FlyerRenderer), not tied
    // to the chosen font file having its own bold/italic variant - most of
    // the 7 bundled fonts are single-weight display faces with no such
    // variant to switch to. Text fields only; meaningless for Image.
    bool Bold = false, bool Italic = false, bool Underline = false,
    // Degrees, clockwise, around the field's own (X,Y) anchor. New - no
    // migration needed since Fields is a JSON column; FlyerRenderer applies
    // it via canvas.RotateDegrees around that same anchor.
    double Rotation = 0,
    // Image fields only - same fake-italic shear DrawTextField's Italic
    // already applies to text, offered for a logo/photo instead since an
    // Image field has no separate bold/underline concept of its own.
    bool Skew = false);

/// <summary>A generated flyer image, usually (not always - see GigRef)
/// keyed to one gig ("one each" - re-flyering a gig just adds a new row,
/// old ones kept for history rather than being deleted). SourceCatalogItemId
/// is nullable and SetNull-deleted: deleting the background image a flyer
/// was built from is allowed (with a warning shown first - see
/// CatalogController/catalog.js), not blocked - the flyer just loses its
/// source and can no longer be re-opened in the editor, while its
/// already-rendered image (GeneratedCatalogItem, a separate Catalog row)
/// stays completely intact.</summary>
public class Flyer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public Guid GeneratedCatalogItemId { get; set; }
    public CatalogItem GeneratedCatalogItem { get; set; } = null!;

    public Guid? SourceCatalogItemId { get; set; }
    public CatalogItem? SourceCatalogItem { get; set; }

    // SiteContentRef.GigRef(gig) - the same stable ref ScheduleItem/GigSet
    // already key off, so a Flyer is correlatable with everything else
    // tied to a gig without a new join concept. Nullable so a flyer can be
    // explicitly disassociated from any gig (FlyersController.Update's
    // "blank" choice in the Gig dropdown) - a reusable/generic flyer, or
    // just a gig's last flyer being removed so that gig has none. Clearing
    // this also clears the old Gig's SelectedFlyerId if this flyer was its
    // live pick (see Update) - a flyer no longer tied to a gig can't stay
    // that gig's "current" one.
    public string? GigRef { get; set; }

    public required List<FlyerFieldDef> Fields { get; set; } = [];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Set only via GigsController.Archive cascading from its owning Gig
    // (see Gig.IsArchived). Unlike GigSet, a Flyer's rendered image is
    // independently browsable via Catalog's own "Flyers" tab, so it needs
    // this flag of its own - otherwise an archived gig's flyers would keep
    // showing up there.
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }
}
