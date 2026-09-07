using BandManager.Data.Entities;
using BandManager.Data.Services;
using Xunit;

namespace BandManager.Data.Tests;

/// <summary>
/// CatalogStore's two static helpers decide how every uploaded/fetched
/// file gets classified and named - used by every upload path in the app
/// (Catalog, gig flyers, media art, gallery images). Pure functions, no DB
/// needed.
/// </summary>
public class CatalogStoreStaticHelperTests
{
    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("video/mp4", "mp4")]
    [InlineData("audio/mpeg", "mp3")]
    public void ExtForMimeType_ReturnsKnownExtension_ForKnownMimeType(string mimeType, string expectedExt)
    {
        Assert.Equal(expectedExt, CatalogStore.ExtForMimeType(mimeType, originalFilename: null));
    }

    [Fact]
    public void ExtForMimeType_FallsBackToOriginalFilenameExtension_WhenMimeTypeUnrecognized()
    {
        var ext = CatalogStore.ExtForMimeType("application/octet-stream", "cover-photo.WEBP");
        Assert.Equal("webp", ext);
    }

    [Fact]
    public void ExtForMimeType_FallsBackToBin_WhenNeitherMimeTypeNorFilenameHelp()
    {
        var ext = CatalogStore.ExtForMimeType("application/octet-stream", originalFilename: null);
        Assert.Equal("bin", ext);
    }

    [Theory]
    [InlineData("image/png", MediaType.Image)]
    [InlineData("video/webm", MediaType.Video)]
    [InlineData("audio/ogg", MediaType.Audio)]
    public void MediaTypeForMime_ClassifiesByPrefix(string mimeType, MediaType expected)
    {
        Assert.Equal(expected, CatalogStore.MediaTypeForMime(mimeType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("application/pdf")]
    public void MediaTypeForMime_ReturnsNull_ForUnsupportedOrMissingMimeType(string? mimeType)
    {
        Assert.Null(CatalogStore.MediaTypeForMime(mimeType));
    }
}
