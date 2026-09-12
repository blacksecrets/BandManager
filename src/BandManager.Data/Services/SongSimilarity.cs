using System.Text.RegularExpressions;

namespace BandManager.Data.Services;

/// <summary>
/// Fuzzy title+artist similarity scoring for the SuperAdmin new-song
/// review flow - "this might already be the same song under a
/// different spelling," a score for a human to judge, not an automatic
/// duplicate resolution. Distinct from SongsController.Match's exact
/// rule (used by the Repertoire/Setlist "add a song" forms to skip an
/// obvious duplicate before creating anything - a yes/no check, not a
/// ranked list) and from Search's free-text substring search.
/// </summary>
public static class SongSimilarity
{
    // Strips case, punctuation, and the common trailing qualifiers that
    // make two otherwise-identical titles look different in a plain
    // string comparison - "(Live)", "(Remastered 2011)", "feat. X".
    private static readonly Regex QualifierPattern = new(
        @"\((?:live|remaster(?:ed)?|acoustic|demo|edit|radio edit|explicit|clean)[^)]*\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FeaturingPattern = new(@"\bfeat\.?\s+.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NonAlphaNumericPattern = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    public static string Normalize(string? text)
    {
        var s = (text ?? "").ToLowerInvariant();
        s = QualifierPattern.Replace(s, "");
        s = FeaturingPattern.Replace(s, "");
        s = NonAlphaNumericPattern.Replace(s, "");
        return s;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return dp[a.Length, b.Length];
    }

    private static double StringSimilarity(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        if (a.Length == 0 || b.Length == 0) return 0;
        return 1.0 - (double)LevenshteinDistance(a, b) / Math.Max(a.Length, b.Length);
    }

    /// <summary>Title-weighted combined similarity (0-1) between two
    /// title+artist pairs, after normalization. Weighted 75/25 toward
    /// title since artist is more often blank/inconsistent than title
    /// is - a threshold around 0.55 has room to tighten once there's
    /// real review data to calibrate against.</summary>
    public static double Score(string title, string? artist, string otherTitle, string? otherArtist)
    {
        var titleSim = StringSimilarity(Normalize(title), Normalize(otherTitle));
        var artistSim = StringSimilarity(Normalize(artist), Normalize(otherArtist));
        return titleSim * 0.75 + artistSim * 0.25;
    }
}
