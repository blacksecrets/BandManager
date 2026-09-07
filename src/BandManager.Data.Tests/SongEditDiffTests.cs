using BandManager.Data.Entities;
using BandManager.Data.Services;
using Xunit;

namespace BandManager.Data.Tests;

/// <summary>
/// SongEditDiff is the one piece of logic shared by two very differently-
/// shaped callers (a single-song edit proposal, and a Band Admin's CSV
/// import merge) - see its own doc comment. A bug here would either miss a
/// real change (silently dropping an edit) or flag a false change (forcing
/// every CSV row through review even when nothing differs).
/// </summary>
public class SongEditDiffTests
{
    private static Dictionary<string, SongFieldChange> BuildNoChange() =>
        SongEditDiff.Build(
            currentTitle: "Same Title", currentArtist: "Same Artist", currentAlbum: "Same Album", currentKey: "Em", currentLengthSeconds: 200,
            currentYouTubeUrl: "https://yt.example.com", currentSpotifyUrl: "https://sp.example.com", currentSongsterrUrl: "https://ss.example.com",
            newTitle: "Same Title", newArtist: "Same Artist", newAlbum: "Same Album", newKey: "Em", newLengthSeconds: 200,
            newYouTubeUrl: "https://yt.example.com", newSpotifyUrl: "https://sp.example.com", newSongsterrUrl: "https://ss.example.com");

    [Fact]
    public void Build_ReturnsEmpty_WhenNothingChanged()
    {
        Assert.Empty(BuildNoChange());
    }

    [Fact]
    public void Build_DetectsOnlyTheFieldsThatActuallyChanged()
    {
        var changes = SongEditDiff.Build(
            currentTitle: "Old Title", currentArtist: "Same Artist", currentAlbum: null, currentKey: null, currentLengthSeconds: null,
            currentYouTubeUrl: null, currentSpotifyUrl: null, currentSongsterrUrl: null,
            newTitle: "New Title", newArtist: "Same Artist", newAlbum: null, newKey: null, newLengthSeconds: null,
            newYouTubeUrl: null, newSpotifyUrl: null, newSongsterrUrl: null);

        var change = Assert.Single(changes);
        Assert.Equal(nameof(Song.Title), change.Key);
        Assert.Equal("Old Title", change.Value.OldValue);
        Assert.Equal("New Title", change.Value.NewValue);
    }

    [Fact]
    public void Build_TreatsLengthSecondsAsChanged_WhenGoingFromNullToAValue()
    {
        var changes = SongEditDiff.Build(
            currentTitle: "T", currentArtist: null, currentAlbum: null, currentKey: null, currentLengthSeconds: null,
            currentYouTubeUrl: null, currentSpotifyUrl: null, currentSongsterrUrl: null,
            newTitle: "T", newArtist: null, newAlbum: null, newKey: null, newLengthSeconds: 245,
            newYouTubeUrl: null, newSpotifyUrl: null, newSongsterrUrl: null);

        var change = Assert.Single(changes);
        Assert.Equal(nameof(Song.LengthSeconds), change.Key);
        Assert.Null(change.Value.OldValue);
        Assert.Equal("245", change.Value.NewValue);
    }

    [Fact]
    public void Build_TreatsLengthSecondsAsChanged_WhenGoingFromAValueToNull()
    {
        var changes = SongEditDiff.Build(
            currentTitle: "T", currentArtist: null, currentAlbum: null, currentKey: null, currentLengthSeconds: 180,
            currentYouTubeUrl: null, currentSpotifyUrl: null, currentSongsterrUrl: null,
            newTitle: "T", newArtist: null, newAlbum: null, newKey: null, newLengthSeconds: null,
            newYouTubeUrl: null, newSpotifyUrl: null, newSongsterrUrl: null);

        var change = Assert.Single(changes);
        Assert.Equal("180", change.Value.OldValue);
        Assert.Null(change.Value.NewValue);
    }

    [Fact]
    public void Build_AllNullCurrentValues_TreatsEveryNonNullNewFieldAsChanged()
    {
        // The "brand-new song from a CSV row" case (SongCsvImportController's
        // merge path) - every field is new, nothing to diff against.
        var changes = SongEditDiff.Build(
            currentTitle: null, currentArtist: null, currentAlbum: null, currentKey: null, currentLengthSeconds: null,
            currentYouTubeUrl: null, currentSpotifyUrl: null, currentSongsterrUrl: null,
            newTitle: "Brand New Song", newArtist: "New Artist", newAlbum: null, newKey: "G", newLengthSeconds: 210,
            newYouTubeUrl: null, newSpotifyUrl: null, newSongsterrUrl: null);

        Assert.Equal(4, changes.Count); // Title, OriginalArtist, Key, LengthSeconds - Album/URLs stayed null->null
        Assert.Equal("Brand New Song", changes[nameof(Song.Title)].NewValue);
        Assert.Null(changes[nameof(Song.Title)].OldValue);
    }

    [Fact]
    public void Build_MultipleFieldsChanged_AreAllReported()
    {
        var changes = SongEditDiff.Build(
            currentTitle: "T", currentArtist: "A", currentAlbum: "Alb", currentKey: "C", currentLengthSeconds: 100,
            currentYouTubeUrl: "https://old-yt.example.com", currentSpotifyUrl: null, currentSongsterrUrl: null,
            newTitle: "T", newArtist: "A", newAlbum: "New Alb", newKey: "D", newLengthSeconds: 100,
            newYouTubeUrl: "https://new-yt.example.com", newSpotifyUrl: null, newSongsterrUrl: null);

        Assert.Equal(3, changes.Count);
        Assert.True(changes.ContainsKey(nameof(Song.Album)));
        Assert.True(changes.ContainsKey(nameof(Song.Key)));
        Assert.True(changes.ContainsKey(nameof(Song.YouTubeUrl)));
    }
}
