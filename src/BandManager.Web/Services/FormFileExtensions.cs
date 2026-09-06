using BandManager.Data.Services;

namespace BandManager.Web.Services;

public static class FormFileExtensions
{
    /// <summary>Reads an IFormFile into the plain-bytes payload
    /// CatalogStore.ResolveMediaInputAsync expects - IFormFile is ASP.NET
    /// Core-specific, not available to BandManager.Data's plain class
    /// library project, so this conversion happens here at the Web
    /// layer.</summary>
    public static async Task<UploadedFilePayload?> ToUploadedFilePayloadAsync(this IFormFile? file)
    {
        if (file is null || file.Length == 0) return null;
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        return new UploadedFilePayload(ms.ToArray(), file.ContentType, file.FileName);
    }
}
