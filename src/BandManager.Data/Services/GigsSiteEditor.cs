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
public class GigsSiteEditor(GitHubSiteClient gitHub)
{
    private const string FilePath = "js/calendar.js";

    private static string BuildBlockText(Gig gig, List<WithAct> withActs)
    {
        var lines = new List<string> { "    {", $"        id: \"{EscapeForQuotes(gig.Ref)}\",", $"        date: \"{EscapeForQuotes(gig.Date)}\"," };
        if (!string.IsNullOrEmpty(gig.Time)) lines.Add($"        time: \"{EscapeForQuotes(gig.Time)}\",");
        lines.Add($"        title: \"{EscapeForQuotes(gig.Title)}\",");
        if (withActs.Count > 0)
        {
            var rawArray = "[" + string.Join(", ", withActs.Select(w =>
                $"{{ name: \"{EscapeForQuotes(w.Name ?? "")}\", url: \"{EscapeForQuotes(w.Url ?? "")}\" }}")) + "]";
            lines.Add($"        with: {rawArray},");
        }
        if (!string.IsNullOrEmpty(gig.DoorsTime)) lines.Add($"        doorsTime: \"{EscapeForQuotes(gig.DoorsTime)}\",");
        if (!string.IsNullOrEmpty(gig.OpenerTime)) lines.Add($"        openerTime: \"{EscapeForQuotes(gig.OpenerTime)}\",");
        if (!string.IsNullOrEmpty(gig.HeadlinerTime)) lines.Add($"        headlinerTime: \"{EscapeForQuotes(gig.HeadlinerTime)}\",");
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
