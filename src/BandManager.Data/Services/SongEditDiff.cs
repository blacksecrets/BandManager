using BandManager.Data.Entities;

namespace BandManager.Data.Services;

/// <summary>
/// Builds the field-level diff a SongEditRequest stores, shared by every
/// path that can propose a change to a shared Song: a single-song edit
/// (SongsController.ProposeEdit) and a Band Admin's CSV import merge
/// (RepertoireController.Import). Passing all-null "current" values
/// diffs against nothing - exactly what a brand-new song from a CSV row
/// needs (every field is "new", nothing to show as the old value).
/// </summary>
public static class SongEditDiff
{
    public static Dictionary<string, SongFieldChange> Build(
        string? currentTitle, string? currentArtist, string? currentAlbum, string? currentKey, int? currentLengthSeconds,
        string? currentYouTubeUrl, string? currentSpotifyUrl, string? currentSongsterrUrl,
        string newTitle, string? newArtist, string? newAlbum, string? newKey, int? newLengthSeconds,
        string? newYouTubeUrl, string? newSpotifyUrl, string? newSongsterrUrl)
    {
        var changes = new Dictionary<string, SongFieldChange>();
        void Diff(string field, string? oldValue, string? newValue)
        {
            if (oldValue != newValue) changes[field] = new SongFieldChange(oldValue, newValue);
        }
        Diff(nameof(Song.Title), currentTitle, newTitle);
        Diff(nameof(Song.OriginalArtist), currentArtist, newArtist);
        Diff(nameof(Song.Album), currentAlbum, newAlbum);
        Diff(nameof(Song.Key), currentKey, newKey);
        if (currentLengthSeconds != newLengthSeconds)
            changes[nameof(Song.LengthSeconds)] = new SongFieldChange(currentLengthSeconds?.ToString(), newLengthSeconds?.ToString());
        Diff(nameof(Song.YouTubeUrl), currentYouTubeUrl, newYouTubeUrl);
        Diff(nameof(Song.SpotifyUrl), currentSpotifyUrl, newSpotifyUrl);
        Diff(nameof(Song.SongsterrUrl), currentSongsterrUrl, newSongsterrUrl);
        return changes;
    }
}
