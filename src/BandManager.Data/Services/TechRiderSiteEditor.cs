using System.Net;
using System.Text.RegularExpressions;
using BandManager.Data.Entities;
using static BandManager.Data.Services.SiteTextEditing;

namespace BandManager.Data.Services;

/// <summary>
/// Publishes one Act's Tech Rider PDF to a Band's connected site and
/// best-effort patches epk.html's Downloads section to link it - mirrors
/// GigsSiteEditor's own "best-effort, one-way, called after the DB save"
/// shape. A band with no site configured simply never calls this (see
/// TechRiderController's HasSiteConfiguredAsync check, same pattern
/// GigsController already uses).
///
/// The PDF's path is derived, not hand-picked, so the Band's existing
/// IsDefault Act publishes to the *exact* path its real, already-live
/// Tech Rider link already uses with zero epk.html changes needed for
/// the common single-Act case: stripping everything but letters/digits
/// from "Black Secrets" reproduces "BlackSecrets" - e.g.
/// doc/BlackSecretsTechRider.pdf, byte-for-byte the path Black Secrets'
/// site already links to today. A second Act's name is appended the same
/// way (doc/BlackSecretsUnpluggedTechRider.pdf for an "Unplugged" Act).
/// </summary>
public class TechRiderSiteEditor(GitHubSiteClient gitHub)
{
    private static string AlphaNumeric(string text) => Regex.Replace(text, "[^A-Za-z0-9]", "");

    public static string PdfPath(Band band, Act act)
    {
        var bandPart = AlphaNumeric(band.Name);
        var actPart = act.IsDefault ? "" : AlphaNumeric(act.Name);
        return $"doc/{bandPart}{actPart}TechRider.pdf";
    }

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>Pushes the PDF bytes, then best-effort patches epk.html's
    /// Downloads section - a missing epk.html, or one with no Downloads
    /// section, is a no-op there (not an error): the PDF itself is still
    /// live at its stable path either way, this is purely a convenience
    /// link.</summary>
    public async Task PublishAsync(Band band, Act act, byte[] pdfBytes)
    {
        var path = PdfPath(band, act);
        var sha = await gitHub.GetFileShaAsync(band, path);
        await gitHub.PutBinaryFileAsync(band, path, pdfBytes, $"Update Tech Rider for {act.Name}", sha);

        try { await PatchDownloadsSectionAsync(band, act, path); }
        catch (InvalidOperationException) { /* best-effort - PDF push above already succeeded either way */ }
    }

    private async Task PatchDownloadsSectionAsync(Band band, Act act, string pdfPath)
    {
        var epk = await gitHub.GetFileAsync(band, "epk.html");
        if (epk is null) return;

        var section = FindHtmlElementBlock(epk.Content, "downloads");
        if (section is null) return;

        var anchorId = $"tech-rider-{act.Id}";
        var label = act.IsDefault ? "Download Tech Rider" : $"Download Tech Rider - {WebUtility.HtmlEncode(act.Name)}";
        var description = act.IsDefault
            ? "Click this to download our Tech Rider, including stage plot and all technical details:"
            : $"Click this to download our Tech Rider for {WebUtility.HtmlEncode(act.Name)}, including stage plot and all technical details:";
        var paragraph =
            $"\t\t\t<p id=\"{anchorId}\">\n" +
            $"\t\t\t{description}\n" +
            $"\t\t\t<a href=\"{pdfPath}\" download=\"{FileName(pdfPath)}\" class=\"download-btn\" target=\"_blank\">{label}</a>\n" +
            "\t\t\t</p>";

        var existingParagraph = FindHtmlElementBlock(section.Text, anchorId);

        // First publish for this Act: if a paragraph already links to
        // this exact PDF path - e.g. Black Secrets' own pre-existing,
        // hand-authored Tech Rider link, which this Act's derived path
        // matches byte-for-byte - adopt that paragraph in place instead
        // of adding a second, near-duplicate one. Matches this class's
        // "zero epk.html changes needed for the common single-Act case"
        // goal for real: a derived path matching isn't enough on its
        // own if the existing markup is never actually found and reused.
        if (existingParagraph is null)
        {
            var hrefMatch = Regex.Match(section.Text, $@"<a\b[^>]*\bhref\s*=\s*""{Regex.Escape(pdfPath)}""[^>]*>.*?</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (hrefMatch.Success)
            {
                var pOpen = section.Text.LastIndexOf("<p", hrefMatch.Index, StringComparison.OrdinalIgnoreCase);
                var pCloseMatch = Regex.Match(section.Text[hrefMatch.Index..], @"</p\s*>", RegexOptions.IgnoreCase);
                if (pOpen >= 0 && pCloseMatch.Success)
                {
                    var pClose = hrefMatch.Index + pCloseMatch.Index + pCloseMatch.Length;
                    existingParagraph = new TextBlock(pOpen, pClose, section.Text[pOpen..pClose]);
                }
            }
        }

        string newSectionText;
        if (existingParagraph is not null)
        {
            newSectionText = section.Text[..existingParagraph.Start] + paragraph + section.Text[existingParagraph.End..];
        }
        else
        {
            var closeMatches = Regex.Matches(section.Text, @"</section\b[^>]*>", RegexOptions.IgnoreCase);
            if (closeMatches.Count == 0) return; // FindHtmlElementBlock already found this section, so this shouldn't happen
            var insertPos = closeMatches[^1].Index;
            newSectionText = section.Text[..insertPos] + paragraph + "\n\t\t" + section.Text[insertPos..];
        }

        var newContent = epk.Content[..section.Start] + newSectionText + epk.Content[section.End..];
        await gitHub.PutTextFileAsync(band, "epk.html", newContent, $"Update Tech Rider download link for {act.Name}", epk.Sha);
    }
}
