using BandManager.Data.Entities;
using static BandManager.Data.Services.SiteTextEditing;

namespace BandManager.Data.Services;

/// <summary>
/// Edits a Band's own public site's js/calendar.js - ported from the old
/// app's siteEditor.js. Only ever replaces a field that already exists in
/// a gig's block, or appends one of a known-safe set of optional fields
/// (CreatableFields below) - never injects a field it doesn't recognize,
/// to avoid guessing at structure that isn't there.
/// </summary>
public class GigsSiteEditor(GitHubSiteClient gitHub, GigsSource gigsSource)
{
    private const string FilePath = "js/calendar.js";

    // Fields safe to add to a gig block that has never had them - a show
    // may go from no-tickets-yet to on-sale, or gain a venue link/with-
    // artists detail later. Every other field is strictly replace-only.
    private static readonly HashSet<string> CreatableFields =
    [
        "ticketsUrl", "customTicketsText", "ticketMode", "address",
        "venueUrl", "time", "withArtists", "withArtistsUrl", "flyerMain",
        "doorsTime", "openerTime", "headlinerTime"
    ];

    private static string ApplyField(string blockText, string field, string value)
    {
        if (HasField(blockText, field)) return SetField(blockText, field, value);
        if (CreatableFields.Contains(field)) return AppendField(blockText, field, $"\"{EscapeForQuotes(value)}\"");
        return blockText;
    }

    private static TextBlock? FindGigBlock(string code, Gig gig) =>
        !string.IsNullOrEmpty(gig.Id) ? FindBlock(code, "id", gig.Id) : FindBlock(code, "title", gig.Title);

    /// <summary>fields: only keys present are changed. `title` can itself
    /// be one of the fields being changed without affecting the lookup,
    /// since that's keyed on the gig's stable id, not its title.</summary>
    public async Task UpdateGigFieldsAsync(Band band, Gig gig, Dictionary<string, string> fields)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read calendar.js from the site repo.");

        var block = FindGigBlock(file.Content, gig)
            ?? throw new InvalidOperationException($"Couldn't find \"{gig.Id ?? gig.Title}\" in calendar.js - it may have already changed on the site.");

        var newBlock = block.Text;
        foreach (var (field, value) in fields)
        {
            newBlock = ApplyField(newBlock, field, value);
        }

        var newContent = file.Content[..block.Start] + newBlock + file.Content[block.End..];
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Update {fields.GetValueOrDefault("title", gig.Title)} listing", file.Sha);
        gigsSource.InvalidateCache(band.Id);
    }

    /// <summary>Writes the gig's `with` field as a JS array-of-objects
    /// literal - [{name:"...",url:"..."}, ...] - replacing it in place if
    /// it already exists as an array (SetArrayField), or appending it as a
    /// brand-new field otherwise (AppendField - shape-agnostic, takes raw
    /// JS text). Never touches the legacy withArtists/withArtistsUrl
    /// scalar fields - see Gig.EffectiveWith's doc comment for why leaving
    /// them stale alongside a populated `with` array is harmless.</summary>
    public async Task UpdateGigWithAsync(Band band, Gig gig, List<WithAct> withActs)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read calendar.js from the site repo.");
        var block = FindGigBlock(file.Content, gig)
            ?? throw new InvalidOperationException($"Couldn't find \"{gig.Id ?? gig.Title}\" in calendar.js - it may have already changed on the site.");

        var rawArray = "[" + string.Join(", ", withActs.Select(w =>
            $"{{ name: \"{EscapeForQuotes(w.Name ?? "")}\", url: \"{EscapeForQuotes(w.Url ?? "")}\" }}")) + "]";

        var newBlock = SetArrayField(block.Text, "with", rawArray);
        if (newBlock == block.Text) // field didn't exist as an array yet - first write
            newBlock = AppendField(block.Text, "with", rawArray);

        var newContent = file.Content[..block.Start] + newBlock + file.Content[block.End..];
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Update supporting acts for \"{gig.Title}\"", file.Sha);
        gigsSource.InvalidateCache(band.Id);
    }

    private static string VenueSlug(string venue)
    {
        var slug = Slugify(venue, "gig");
        return slug.StartsWith("the-") ? slug["the-".Length..] : slug;
    }

    public record NewGigFields(
        string Title, string Venue, string Address, string Date, string IsoDate,
        string? VenueUrl, string? Time, string? WithArtists, string? WithArtistsUrl,
        string? TicketMode, string? TicketsUrl, string? CustomTicketsText,
        List<WithAct>? With = null, string? DoorsTime = null, string? OpenerTime = null, string? HeadlinerTime = null);

    /// <summary>Creates a brand-new gig entry. flyerBytes/flyerExt:
    /// optional - a show can be booked before its flyer exists. Returns
    /// the new gig's id and, if a flyer was provided, its site-relative
    /// path.</summary>
    public async Task<(string Id, string? FlyerMain)> AddGigAsync(Band band, NewGigFields fields, byte[]? flyerBytes, string? flyerExt)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read calendar.js from the site repo.");

        var bounds = FindArrayBounds(file.Content, "gigs")
            ?? throw new InvalidOperationException("Could not find the gigs array in calendar.js.");
        var arrayText = file.Content[bounds.Start..bounds.End];

        var id = UniqueId(file.Content, $"{VenueSlug(fields.Venue)}-{fields.IsoDate}");

        string? flyerMain = null;
        if (flyerBytes is not null)
        {
            flyerMain = $"flyers/{id}.{flyerExt}";
            await gitHub.PutBinaryFileAsync(band, flyerMain, flyerBytes, $"Add flyer for \"{fields.Title}\"");
        }

        var lines = new List<string> { "    {", $"        id: \"{id}\",", $"        date: \"{EscapeForQuotes(fields.Date)}\"," };
        if (!string.IsNullOrEmpty(fields.Time)) lines.Add($"        time: \"{EscapeForQuotes(fields.Time)}\",");
        lines.Add($"        title: \"{EscapeForQuotes(fields.Title)}\",");
        if (!string.IsNullOrEmpty(fields.WithArtists)) lines.Add($"        withArtists: \"{EscapeForQuotes(fields.WithArtists)}\",");
        if (!string.IsNullOrEmpty(fields.WithArtistsUrl)) lines.Add($"        withArtistsUrl: \"{EscapeForQuotes(fields.WithArtistsUrl)}\",");
        if (fields.With is { Count: > 0 })
        {
            var rawArray = "[" + string.Join(", ", fields.With.Select(w =>
                $"{{ name: \"{EscapeForQuotes(w.Name ?? "")}\", url: \"{EscapeForQuotes(w.Url ?? "")}\" }}")) + "]";
            lines.Add($"        with: {rawArray},");
        }
        if (!string.IsNullOrEmpty(fields.DoorsTime)) lines.Add($"        doorsTime: \"{EscapeForQuotes(fields.DoorsTime)}\",");
        if (!string.IsNullOrEmpty(fields.OpenerTime)) lines.Add($"        openerTime: \"{EscapeForQuotes(fields.OpenerTime)}\",");
        if (!string.IsNullOrEmpty(fields.HeadlinerTime)) lines.Add($"        headlinerTime: \"{EscapeForQuotes(fields.HeadlinerTime)}\",");
        lines.Add($"        venue: \"{EscapeForQuotes(fields.Venue)}\",");
        if (!string.IsNullOrEmpty(fields.VenueUrl)) lines.Add($"        venueUrl: \"{EscapeForQuotes(fields.VenueUrl)}\",");
        lines.Add($"        address: \"{EscapeForQuotes(fields.Address)}\"");
        if (flyerMain is not null)
        {
            lines[^1] += ",";
            lines.Add($"        flyerMain: \"{EscapeForQuotes(flyerMain)}\"");
        }

        if (fields.TicketMode == "free")
        {
            lines[^1] += ",";
            lines.Add("        ticketMode: \"free\"");
        }
        else if (fields.TicketMode == "custom" && !string.IsNullOrEmpty(fields.CustomTicketsText))
        {
            lines[^1] += ",";
            lines.Add("        ticketMode: \"custom\",");
            lines.Add($"        customTicketsText: \"{EscapeForQuotes(fields.CustomTicketsText)}\"");
        }
        else if (fields.TicketMode == "url" && !string.IsNullOrEmpty(fields.TicketsUrl))
        {
            lines[^1] += ",";
            lines.Add("        ticketMode: \"url\",");
            lines.Add($"        ticketsUrl: \"{EscapeForQuotes(fields.TicketsUrl)}\"");
        }
        lines.Add("    }");
        var blockText = string.Join('\n', lines);

        var newArrayText = InsertBeforeClose(arrayText, ']', blockText)
            ?? throw new InvalidOperationException("Could not figure out where to insert the new gig.");

        var newContent = file.Content[..bounds.Start] + newArrayText + file.Content[bounds.End..];
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Add \"{fields.Title}\" gig", file.Sha);
        gigsSource.InvalidateCache(band.Id);
        return (id, flyerMain);
    }

    /// <summary>Removes the whole gig entry. Doesn't touch the flyer file
    /// left behind in flyers/ - an unused file with no entry pointing at
    /// it is the harmless direction to fail in.</summary>
    public async Task DeleteGigAsync(Band band, Gig gig)
    {
        var file = await gitHub.GetFileAsync(band, FilePath)
            ?? throw new InvalidOperationException("Could not read calendar.js from the site repo.");

        var block = FindGigBlock(file.Content, gig)
            ?? throw new InvalidOperationException($"Couldn't find \"{gig.Id ?? gig.Title}\" in calendar.js - it may have already changed on the site.");

        var newContent = RemoveBlock(file.Content, block);
        await gitHub.PutTextFileAsync(band, FilePath, newContent, $"Remove \"{gig.Title}\" gig", file.Sha);
        gigsSource.InvalidateCache(band.Id);
    }
}
