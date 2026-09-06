using BandManager.Data.Entities;

namespace BandManager.Data.Seed;

/// <summary>
/// Static reference data about each platform type - what's possible, not
/// what's configured. Ported from the old app's src/seedPlatforms.js.
/// Genuinely global now (identical for every Band) since it describes the
/// platform itself, not any one Band's setup - the two spots that used to
/// say "Black Secrets" specifically (Google Business Profile, Bandsintown)
/// are genericized to "your band's" here.
/// </summary>
public static class PlatformSeedData
{
    private const string MetaInstructions = """
        <p class="drift-note">Meta changes these screens often, so this deliberately isn't a click-by-click list - those go stale fast. It names what's stable: the access you need, what to type in here, and the exact permission names to grant. Tap the <span class="info-icon" data-help="Meta reorganizes and renames things in this console constantly - permission names alone have drifted mid-project before. If something below doesn't match what you see, describe exactly what's on your screen and ask - don't guess.">i</span> on each block for more.</p>

        <div class="instructions-block">
            <h4>Facebook access you need <span class="info-icon" data-help="Full control unlocks managing what permissions apps like this one can have on the Page. Task-based access (Content, Messages, Ads, Insights) isn't enough, even for an owner-adjacent role - Permissions specifically is what's missing if setup fails. Check this in Meta Business Suite: Settings, then People - your own row lists what you have. If Permissions isn't listed, whoever does have full control needs to add it to your access from their own login.">i</span></h4>
            <p>Whoever does this setup needs <strong>Full control</strong> on the Facebook Page - specifically the <strong>Permissions</strong> access category, not just Content/Messages/Ads/Insights. If you're missing it, the Page's owner needs to grant it to you; you can't add it to your own access.</p>
        </div>

        <div class="instructions-block">
            <h4>What you'll enter here <span class="info-icon" data-help="The App ID/Secret only matter for the 'Finish setup automatically' shortcut below, which does the token exchange and Page lookup for you. Skip them entirely if you'd rather look up the Page ID and token yourself and use the manual form.">i</span></h4>
            <ul>
                <li><strong>Facebook Page ID</strong> and <strong>Page Access Token</strong> - always required.</li>
                <li><strong>Meta App ID</strong> and <strong>App Secret</strong> - optional, only for the automatic setup shortcut.</li>
            </ul>
        </div>

        <div class="instructions-block">
            <h4>Permissions to grant this app</h4>
            <p><code>pages_show_list</code>, <code>pages_read_engagement</code>, <code>pages_manage_posts</code>, <code>pages_manage_metadata</code>, <code>business_management</code>.</p>
        </div>

        <p class="tile-note">Posting to Instagram doesn't require anything here - see the separate Instagram row below, including an option to reuse this Facebook connection if your Instagram account is linked to this Page.</p>
        """;

    private const string InstagramInstructions = """
        <p>Instagram can connect two different ways - pick whichever matches your situation. You don't need to link Facebook and Instagram together to use this app; that's only required for the first option below.</p>

        <div class="instructions-block">
            <h4>Option A - Linked to your Facebook Page</h4>
            <p>If your Instagram account is already a Business/Creator account linked to your band's Facebook Page, this is the simplest option: it reuses whatever Page Access Token is saved on the Facebook row above, so there's nothing separate to keep in sync. You just need the Instagram account's own ID.</p>
            <ol class="steps">
                <li>Connect Facebook first (row above), if you haven't already.</li>
                <li>In <a href="https://developers.facebook.com/tools/explorer/" target="_blank">Graph API Explorer</a>, with your Page Access Token selected, query <code>me/accounts</code> to find your Page, then query <code>&lt;page-id&gt;?fields=instagram_business_account</code> to get the <strong>Instagram Business Account ID</strong>.</li>
                <li>Paste that ID below with "Linked to Facebook" selected.</li>
            </ol>
        </div>

        <div class="instructions-block">
            <h4>Option B - Independent Instagram connection</h4>
            <p>No Facebook Page link needed at all - Meta's separate <strong>"Instagram API with Instagram Login"</strong> flow issues its own access token tied directly to the Instagram account. Use this if linking Facebook and Instagram together has been a struggle, or if this band's Instagram simply isn't linked to a Facebook Page.</p>
            <ol class="steps">
                <li>In your Meta Developer App, add the <strong>Instagram</strong> product, then under it choose <strong>"API setup with Instagram login"</strong> (not "with Facebook login" - that's Option A's flow, a different app/token system entirely).</li>
                <li>Follow that page's own steps to generate an access token for the account - permissions needed: <code>instagram_business_basic</code>, <code>instagram_business_content_publish</code>.</li>
                <li>That same setup page shows the account's <strong>Instagram User ID</strong> - copy both it and the token below with "Independent connection" selected.</li>
            </ol>
        </div>
        """;

    private const string GbpInstructions = """
        <ol class="steps">
            <li>Go to the <a href="https://console.cloud.google.com/" target="_blank">Google Cloud Console</a> and create a project (or use an existing one).</li>
            <li>Enable the <strong>Business Profile API</strong> for that project (API Library &rarr; search for it &rarr; Enable).</li>
            <li>Under <strong>APIs &amp; Services &rarr; Credentials</strong>, create an <strong>OAuth 2.0 Client ID</strong> and complete the OAuth consent screen (internal or external, whichever applies to your band's Google Workspace/account setup).</li>
            <li>Authorize that client for the <code>https://www.googleapis.com/auth/business.manage</code> scope and complete the OAuth flow once (e.g. via <a href="https://developers.google.com/oauthplayground/" target="_blank">OAuth Playground</a>, using your own client ID/secret under its settings gear icon) to get an access token.</li>
            <li>Find the <strong>Account ID</strong> and <strong>Location ID</strong> for your band's Business Profile - either from the Playground's <code>accounts.list</code> / <code>accounts.locations.list</code> calls, or from the Business Profile dashboard URL.</li>
            <li>Access tokens from this flow expire quickly (about an hour) - paste in a fresh one whenever posting stops working; a longer-term setup would use the refresh token instead, which can be added here later if this becomes a hassle.</li>
        </ol>
        """;

    private const string NoApiNote = "<p>No credentials needed - there's no usable posting API for this. \"Fart it out\" just records that you posted it yourself elsewhere.</p>";

    private const string BandsintownInstructions = """
        <p>No posting API here either - "Fart it out" still just records that you posted it yourself. This saves the artist/app id used by your site's "Get Tour Updates" widget, so it stays correct without hand-editing anything.</p>
        <ol class="steps">
            <li>Log into <a href="https://artists.bandsintown.com/" target="_blank">artists.bandsintown.com</a> as your band's artist account.</li>
            <li>Find your <strong>Artist ID</strong> and <strong>App ID</strong> from the widget embed code (Tools &rarr; Widgets, or the existing embed on your site).</li>
            <li>Paste both below and save - this pushes them straight to the live site.</li>
        </ol>
        """;

    private const string GitHubInstructions = """
        <p>Lets this app read your site's current gigs/media/gallery (needed for Gig Sets and the Website tiles) and push flyer/calendar/media/gallery edits straight to the live site - no separate manual git push. Four fields below, all required for this to work:</p>
        <ol class="steps">
            <li>
                <strong>Site base URL</strong>: the live web address fans actually visit, e.g. <code>https://yourband.com</code> - no trailing slash, no <code>/js/calendar.js</code> on the end. This app reads <code>js/calendar.js</code>/<code>js/media.js</code>/<code>js/gallery.js</code> from underneath it directly.
            </li>
            <li>
                <strong>GitHub owner</strong> and <strong>GitHub repo</strong>: the two parts of your website's GitHub repository URL - if your repo is at <code>github.com/your-org/your-site-repo</code>, the owner is <code>your-org</code> and the repo is <code>your-site-repo</code>. This only works if pushing to that repo's default branch (main/master) actually redeploys the live site (GitHub Pages, Netlify/Vercel's GitHub integration, etc.) - if it doesn't auto-deploy, a saved edit here will update the repo but not the live site until something else republishes it.
            </li>
            <li>
                <strong>GitHub Token</strong>:
                <ol>
                    <li>Go to <a href="https://github.com/settings/personal-access-tokens/new" target="_blank">github.com/settings/personal-access-tokens/new</a> (log in as an account with write access to your website repository).</li>
                    <li><strong>Token name</strong>: anything recognizable, e.g. "BandManager".</li>
                    <li><strong>Expiration</strong>: pick a date (90 days, 1 year, etc - GitHub requires an actual expiry for fine-grained tokens on personal accounts).</li>
                    <li><strong>Repository access</strong>: select <strong>Only select repositories</strong> &rarr; choose your website repository.</li>
                    <li><strong>Permissions</strong>: expand <strong>Repository permissions</strong> &rarr; set <strong>Contents</strong> to <strong>Read and write</strong>.</li>
                    <li>Click <strong>Generate token</strong> and copy it - GitHub only shows it once.</li>
                </ol>
            </li>
            <li>Fill in all four fields below and save.</li>
        </ol>
        """;

    public static readonly Platform[] All =
    [
        new() { Id = "facebook", DisplayName = "Facebook", SupportsPosting = true, CredentialFields = ["pageId", "pageAccessToken"], SetupInstructions = MetaInstructions, SortOrder = 1 },
        // igUserId is the only field CredentialsController.Save enforces
        // generically - "mode" and the standalone-only "igAccessToken" get
        // special-cased there instead, since which fields are required
        // depends on which of the two connection modes was picked (see
        // InstagramInstructions above and InstagramPublisher.RequireCredsAsync).
        new() { Id = "instagram", DisplayName = "Instagram", SupportsPosting = true, CredentialFields = ["igUserId"], SetupInstructions = InstagramInstructions, SortOrder = 2 },
        new() { Id = "tiktok", DisplayName = "TikTok", SupportsPosting = false, CredentialFields = null, SetupInstructions = NoApiNote, SortOrder = 3 },
        new() { Id = "youtube", DisplayName = "YouTube", SupportsPosting = false, CredentialFields = null, SetupInstructions = NoApiNote, SortOrder = 4 },
        new() { Id = "bandsintown", DisplayName = "Bandsintown/Songkick", SupportsPosting = false, CredentialFields = ["artistName", "appId"], SetupInstructions = BandsintownInstructions, SortOrder = 5 },
        new() { Id = "googleBusiness", DisplayName = "Google Business Profile", SupportsPosting = true, CredentialFields = ["accountId", "locationId", "accessToken"], SetupInstructions = GbpInstructions, SortOrder = 6 },
        new() { Id = "spotify", DisplayName = "Spotify/Apple Music", SupportsPosting = false, CredentialFields = null, SetupInstructions = NoApiNote, SortOrder = 7 },
        new() { Id = "website", DisplayName = "Website", SupportsPosting = true, CredentialFields = ["githubToken"], SetupInstructions = GitHubInstructions, SortOrder = 8 }
    ];
}
