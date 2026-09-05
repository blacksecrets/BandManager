namespace BandManager.Data.Entities;

public enum MediaType { Image, Video, Audio }

public enum CatalogSource { Upload, Url, FrameCapture, Trim, Split, CoverPhoto }

/// <summary>
/// A persistent, browsable, per-Band library of every image/video/audio
/// file this Band's users have touched - fed automatically by every
/// upload spot (mirrors the old app's catalogStore.js auto-registration).
/// Picking an item elsewhere always copies its bytes, so this table's own
/// rows are independent of anything that used a copy.
/// </summary>
public class CatalogItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public MediaType MediaType { get; set; }
    public required string FilePath { get; set; }
    public string? ThumbnailPath { get; set; } // images only
    public string? OriginalFilename { get; set; }
    public string? Label { get; set; }
    public required string MimeType { get; set; }
    public long FileSize { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }

    public CatalogSource Source { get; set; }
    public string? SourceUrl { get; set; } // set when Source == Url
    public string? UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
