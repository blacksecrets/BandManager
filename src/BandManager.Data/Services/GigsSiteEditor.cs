using BandManager.Data;
using BandManager.Data.Entities;
using static BandManager.Data.Services.SiteTextEditing;

namespace BandManager.Data.Services;

/// <summary>
/// Publishes a DB Gig row (the source of truth - see Gig.cs) to a Band's
/// public site's js/calendar.js, best-effort and one-way: called *after*
/// GigsController has already saved the DB row, never the other way
/// around, and a failure here doesn't undo that save (see
/// GigsController's write methods, which reuse FlyersController's
/// established "row already saved even if the site push failed" pattern).
/// A band with no site configured simply never calls this.
///
/// Unlike the pre-DB-migration version of this class, which patched
/// individual fields into whatever a gig's existing block happened to
/// contain, PublishGigAsync always rewrites the entire block from the
/// DB row's current full state - simpler, and guarantees the site can
/// never silently drift from the database now that the database is
/// authoritative.
/// </summary>
public class GigsSiteEditor(GitHubSiteClient gitHub, FlyerCache flyerCache) : IGigSitePublisher
{
    private const string FilePath = "js/calendar.js";

    /// <summary>Best-effort pushes already-rendered flyer bytes to
    /// gig.FlyerMain's path - creating it (as flyers/{gig.Ref}.png) on the
    /// gig's first flyer push, else overwriting in place by sha. The exact
    /// branch FlyersController.Create and GigsController's selected-flyer
    /// endpoint both need, extracted here so neither duplicates the
    /// GitHub-push/cache-write pair. Throws InvalidOperationException on a
    /// push failure (propagated from GitHubSiteClient) - callers decide for
    /// themselves what "already saved, but this could not push" means in
    /// their own context, so this doesn't swallow it.</summary>
    public async Task PushFlyerImageAsync(Band band, Gig gig, byte[] rendered, string commitMessage)
    {
        if (string.IsNullOrEmpty(gig.FlyerMain))
        {
            var newPath = $"flyers/{gig.Ref}.png";
            await gitHub.PutBinaryFileAsync(band, newPath, rendered, commitMessage);
            gig.FlyerMain = newPath;
            await flyerCache.WriteDirectlyAsync(band, newPath, rendered);
        }
        else
        {
            var sha = await gitHub.GetFileShaAsync(band, gig.FlyerMain);
            await gitHub.PutBinaryFileAsync(band, gig.FlyerMain, rendered, commitMessage, sha);
            await flyerCache.WriteDirectlyAsync(band, gig.FlyerMain, rendered);
        }
        gig.UpdatedAt = DateTime.UtcNow;
    }

    private static string BuildBlockText(Gig gig, List<WithAct> withActs)
    {
        var lines = new List<string> { "    {", $"        id: \"{EscapeForQuotes(gig.Ref)}\",", $"        date: \"{EscapeForQuotes(GigDateTimeFormatting.FormatDate(gig.Date))}\"," };
        if (!string.IsNullOrEmpty(gig.Time)) lines.Add($"        time: \"{EscapeForQuotes(gig.Time)}\",");
        lines.Add($"        title: \"{EscapeForQuotes(gig.Title)}\",");
        if (withActs.Count > 0)
        {
            var rawArray = "[" + string.Join(", ", withActs.Select(w =>
                $"{{ name: \"{EscapeForQuotes(w.Name ?? "")}\", url: \"{EscapeForQuotes(w.Url ?? "")}\" }}")) + "]";
            lines.Add($"        with: {rawArray},");
        }
        if (GigDateTimeFormatting.FormatTime(gig.DoorsTime) is { } doorsTime) lines.Add($"        doorsTime: \"{EscapeForQuotes(doorsTime)}\",");
        if (GigDateTimeFormatting.FormatTime(gig.OpenerTime) is { } openerTime) lines.Add($"        openerTime: \"{EscapeForQuotes(openerTime)}\",");
        if (GigDateTimeFormatting.FormatTime(gig.HeadlinerTime) is { } headlinerTime) lines.Add($"        headlinerTime: \"{EscapeForQuotes(headlinerTime)}\",");
        lines.Add($"        venue: \"{EscapeForQuotes(gig.Venue ?? "")}\",");
        if (!string.IsNullOrEmpty(gig.VenueUrl)) lines.Add($"        venueUrl: \"{EscapeForQuotes(gig.VenueUrl)}\",");
        lines.Add($"        address: \"{EscapeForQuotes(gig.Address ?? "")}\"");
        if (!string.IsNullOrEmpty(gig.FlyerMain))
        {
            lines[^1] += ",";
            lines.Add($"        flyerMain: \"{EscapeForQuotes(gig.FlyerMain)}\"");
        }

        if (gig.TicketMode == "free")
        {
            lines[^1] += ",";
            lines.Add("        ticketMode: \"free\"");
        }
        else if (gig.TicketMode == "custom" && !string.IsNullOrEmpty(gig.CustomTicketsText))
        {
            lines[^1] += ",";
            lines.Add("        ticketMode: \"custom\",");
            lines.Add($"        customTicketsText: \"{EscapeForQuotes(gig.CustomTicketsText)}\"");
        }
        else if (gig.TicketMode == "url" && !string.IsNullOrEmpty(gig.TicketsUrl))
        {
            lines[^1] += ",";
            lines.Add("        ticketMode: \"url\",");
            lines.Add($"        ticketsUrl: \"{EscapeForQuotes(gig.TicketsUrl)}\"");
        }
        lines.Add("    }");
        return string.Join('\n', lines);
    }

    /// <summary>Writes gig's current full state to calendar.js - replacing
    /// its existing block (matched by id == gig.Ref) if present, else
    /// appending a new one. withActs is passed in resolved (real Band
    /// names looked up from GigWithBand) rather than re-queried here, so
    /// this stays a pure text-publishing concern with no DB dependency of
    /// its own.</summary>
    public async Task PublishGigAsync(Band band, Gig gig, List<WithAct> withActs)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read calendar.js from the site repo.");

        var blockText = BuildBlockText(gig, withActs);
        var existingBlock = FindBlock(file.Content, "id", gig.Ref);

        string newContent;
        if (existingBlock is not null)
        {
            newContent = file.Content[..existingBlock.Start] + blockText + file.Content[existingBlock.End..];
        }
        else
        {
            var bounds = FindArrayBounds(file.Content, "gigs")
                ?? throw new InvalidOperationException("Could not find the gigs array in calendar.js.");
            var arrayText = file.Content[bounds.Start..bounds.End];
            var newArrayText = InsertBeforeClose(arrayText, ']', blockText)
                ?? throw new InvalidOperationException("Could not figure out where to insert the gig.");
            newContent = file.Content[..bounds.Start] + newArrayText + file.Content[bounds.End..];
        }

        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Update \"{gig.Title}\" listing", file.Sha);
    }

    /// <summary>Removes gig's block from calendar.js, if present - a no-op
    /// (not an error) if the site never had it in the first place (e.g. a
    /// gig created before this band had a site configured). Doesn't touch
    /// the flyer file left behind in flyers/ - an unused file with nothing
    /// pointing at it is the harmless direction to fail in.</summary>
    public async Task UnpublishGigAsync(Band band, string gigRef, string gigTitle)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read calendar.js from the site repo.");

        var block = FindBlock(file.Content, "id", gigRef);
        if (block is null) return;

        var newContent = RemoveBlock(file.Content, block);
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Remove \"{gigTitle}\" gig", file.Sha);
    }
}
