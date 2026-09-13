using BandManager.Data.Entities;

namespace BandManager.Data.Seed;

/// <summary>
/// The manual QA checklist content for SuperAdmin's Test Suite page -
/// upserted by DbSeeder the same way every other reference-data seed is
/// (see DbSeeder.cs's own doc comment). Editing wording/steps here and
/// restarting the app updates the tracker in place without touching
/// anyone's already-recorded Status/Notes. Organized by Area in the same
/// order the app's own sidebar nav groups things, so working through this
/// list top to bottom roughly matches a real walkthrough of the app.
/// </summary>
public static class TestCaseSeedData
{
    private static int _n;
    private static TestCase T(string key, string area, string title, string steps, string expected) =>
        new() { Key = key, Area = area, Title = title, Steps = steps, ExpectedResult = expected, SortOrder = _n += 10 };

    public static readonly List<TestCase> All = new();

    static TestCaseSeedData()
    {
        // --- Auth & Onboarding ---
        _n = 0;
        All.AddRange(new[]
        {
            T("auth-login-valid", "Auth & Onboarding", "Log in with a valid account",
              "Go to /login. Enter a real username/password. Submit.",
              "Redirected to the Dashboard (or Profile with a must-change-password banner, if that flag is set). No error shown."),
            T("auth-login-invalid", "Auth & Onboarding", "Log in with a wrong password",
              "Go to /login. Enter a real username with a wrong password. Submit.",
              "A generic 'invalid username or password' error - never reveals whether the username itself was valid."),
            T("auth-forgot-password", "Auth & Onboarding", "Forgot password flow",
              "Click 'Forgot password' on the login page. Enter a real account's email. Submit.",
              "A confirmation message appears regardless of whether the email exists (no account enumeration). If email sending isn't wired to a real provider yet, check server logs for the reset link and confirm it works."),
            T("auth-must-change-password", "Auth & Onboarding", "Forced password change for an admin-created account",
              "As SuperAdmin, create a new user (this sets a temp password and MustChangePassword). Log out. Log in as that new user.",
              "Lands on Profile with a 'your password was set by an admin' banner, not the Dashboard. After changing the password, the banner never reappears."),
            T("auth-logout", "Auth & Onboarding", "Log out",
              "While logged in, click Log out in the sidebar footer.",
              "Session ends and you're returned to the login page. Navigating back to any page redirects to login instead of showing content."),
            T("auth-band-switcher-scope", "Auth & Onboarding", "Band switcher only shows your own bands",
              "Log in as a plain member of exactly one band. Open the band switcher.",
              "Only that one band is listed - no way to see or switch into a band you don't belong to. (As SuperAdmin, every band is listed instead, labeled SuperAdmin.)"),
        });

        // --- Profile ---
        All.AddRange(new[]
        {
            T("profile-username-change", "Profile", "Change username/email",
              "Profile > Your account > change the Username/Email field, save.",
              "Saves successfully and the new value is what you log in with next time; a confirm-change email flow runs if one exists rather than switching instantly."),
            T("profile-name-change", "Profile", "Change first/last name",
              "Profile > Your account > set First name/Last name, save.",
              "Saves; your display name elsewhere in the app (member pickers, notifications, the sidebar footer) updates to the new name."),
            T("profile-password-change", "Profile", "Change your own password",
              "Profile > Your account > enter current password + a new one meeting the strength rule, save.",
              "Succeeds with a matching password; a new password missing an uppercase letter or special character is rejected with a clear message before submit."),
            T("profile-contact-address", "Profile", "Save contact info + address",
              "Profile > Contact info > fill in cell number and a full US address, save.",
              "Saves without requiring address validation to run first. An invalid cell number shows an inline error and blocks save."),
            T("profile-address-validate", "Profile", "Validate address against USPS",
              "Profile > Contact info > enter a real US address, click 'Validate address' (USPS credentials must be configured in SuperAdmin first).",
              "Shows a standardized suggestion with a 'Use this address' button; accepting it fills the fields with the corrected version. With no USPS credentials configured, shows a clear 'not set up yet' note instead of an error."),
            T("profile-avatar-upload", "Profile", "Upload and remove a profile photo",
              "Profile > Your account > upload an image, confirm it appears everywhere your avatar shows (sidebar, member pickers). Then remove it.",
              "Uploaded photo appears immediately; after removal, falls back to the colored-initials circle."),
            T("profile-my-gear-crud", "Profile", "Add, edit, and see gear in My gear",
              "Profile > My gear > add an item with type/make/model/dimensions. Confirm it lists.",
              "New gear item appears in the list and is available to a Band Admin building this member into a Band Gear catalog item ('from-member' copy)."),
            T("profile-gig-prep-defaults", "Profile", "Build a personal Gig Prep default checklist",
              "Profile > Gig Prep Defaults > add a few items across Pre-gig/Packing/Post-gig tabs.",
              "Items save per tab; on a real gig's Gig Prep, 'Load your default' pulls in exactly this list."),
            T("profile-notification-prefs", "Profile", "Save notification preferences",
              "Profile > Notifications > toggle a couple of kinds on/off (in-app vs email), save.",
              "Saves; toggling a kind off means that event no longer creates a notification/email for you going forward."),
            T("profile-roles-readonly", "Profile", "Your roles is read-only",
              "Profile > Your roles, as a plain member.",
              "Shows your role per band you're in, with no way to edit it from here - editing only happens under Band Admin > General > Band Roles."),
        });

        // --- Profile > My Expenses (Travel) ---
        All.AddRange(new[]
        {
            T("travel-cadence-save", "Travel (Expense Tracker)", "Save tax cadence independently",
              "Profile > My Expenses > Travel > pick Yearly or Quarterly, Save cadence (without touching Vehicle).",
              "Cadence saves on its own; the Vehicle section is untouched and doesn't need its own Save click."),
            T("travel-vehicle-save", "Travel (Expense Tracker)", "Save vehicle independently",
              "Profile > My Expenses > Travel > fill in make/model/year/starting mileage, Save vehicle (without touching cadence).",
              "Vehicle saves on its own and persists across a page reload."),
            T("travel-new-trip-home-default", "Travel (Expense Tracker)", "New Trip defaults From=Home, To=manual",
              "Click + New Trip.",
              "'Use my home address' is checked for From (its address fields hidden) and unchecked for To (its fields visible), matching the spec exactly."),
            T("travel-home-address-incomplete", "Travel (Expense Tracker)", "Saving a Home trip with no profile address yet",
              "As a user with no address in Profile, create a trip using Home for From, fill in a valid To, Save.",
              "A modal pops asking you to complete your home address (a validated, complete address) before the trip can save. Completing it there lets the trip save immediately after, without re-opening the trip modal."),
            T("travel-home-snapshot-frozen", "Travel (Expense Tracker)", "Home address is a snapshot, not live-linked",
              "Create a trip using Home. Note the address shown. Go change your home address in Profile. Re-open that same trip.",
              "The trip still shows the OLD home address it was saved with - it does not update to your new profile address."),
            T("travel-location-reuse-prompt", "Travel (Expense Tracker)", "Reusing a previously-used location name",
              "Create a trip with a manually-typed To name/address (not Home). Create a second trip using the exact same name but different address text.",
              "A prompt appears offering the previously-saved address for that name; choosing 'yes' uses the saved address, choosing 'no' lets you type a distinct new name instead."),
            T("travel-round-trip-doubling", "Travel (Expense Tracker)", "Round trip label and mileage",
              "Create a trip with Google Maps configured, check Round trip, Save.",
              "Grid shows 'Round Trip' on that row and the Distance/total mileage is double the one-way distance."),
            T("travel-reason-gig-selector", "Travel (Expense Tracker)", "Gig reason shows a gig picker",
              "In the trip modal, select Trip reason = Gig.",
              "A gig dropdown appears (scoped to the active band); after saving, the grid's Reason column shows the gig's title, not the word 'Gig'."),
            T("travel-reason-other-textbox", "Travel (Expense Tracker)", "Other reason requires text",
              "Select Trip reason = Other and try to Save with the reason text blank.",
              "Save stays disabled/blocked until a short reason is typed; after saving, the grid shows that typed text."),
            T("travel-distance-unconfigured", "Travel (Expense Tracker)", "Distance calc degrades gracefully with no API key",
              "With no Google Maps key configured in SuperAdmin, save a trip with two real addresses.",
              "Trip saves successfully; Distance shows as blank/— rather than blocking the save or showing an error."),
            T("travel-footer-total", "Travel (Expense Tracker)", "Footer totals the current tax period",
              "With several trips logged across different dates, check the grid's footer.",
              "Footer shows total mileage for the CURRENT year (if cadence=Yearly) or current quarter (if Quarterly) - not an all-time total."),
            T("travel-edit-trip", "Travel (Expense Tracker)", "Edit an existing trip",
              "Click an existing row in the Trips grid, change the round-trip checkbox, Save.",
              "The same modal opens pre-filled with that trip's data; saving updates the existing row (no duplicate row created)."),
        });

        // --- Band Flow > Web Presence (Dashboard) ---
        All.AddRange(new[]
        {
            T("dashboard-loads-per-band", "Web Presence (Dashboard)", "Dashboard reflects the active band",
              "Switch bands, watch the Dashboard content change.",
              "Kanban/platform tiles reload for the newly-selected band; no leftover content from the previous band."),
            T("dashboard-no-band-state", "Web Presence (Dashboard)", "No band selected state",
              "As SuperAdmin with no band actively selected, open the Dashboard.",
              "Shows a clear 'select a band' prompt instead of an error or a blank/broken page."),
            T("dashboard-mark-done-cancel", "Web Presence (Dashboard)", "Mark a scheduled post Done or Cancel/Archive",
              "On a pending item, click Mark Done. On another, click Cancel/Archive.",
              "Done moves it out of the pending list; Cancel/Archive removes it without posting anywhere. Both persist after a reload."),
            T("dashboard-assign-item", "Web Presence (Dashboard)", "Assign a pending item to a member",
              "On an unassigned item, click Assign and pick a band member.",
              "Item shows that member's name instead of 'unassigned'; persists after reload."),
        });

        // --- Band Flow > Gig Management ---
        All.AddRange(new[]
        {
            T("gig-add-basic", "Gig Management", "Add a new gig with a new venue",
              "+ Add a Gig > fill Title, enter a brand-new venue name + full address, pick a date, submit.",
              "Gig appears in Upcoming; the venue is now also in the band's venue book for reuse next time."),
            T("gig-edit-gig", "Gig Management", "Edit an existing gig's basic info",
              "Select a gig > Edit Gig > change title/time, save.",
              "Detail view and tile both reflect the new title/time immediately."),
            T("gig-archive-modal-ramifications", "Gig Management", "Archiving a gig always confirms",
              "Select a gig with no flyer/setlist/prep attached yet. Click Archive.",
              "A confirm modal still appears (even with nothing else attached) with a 'Remove from Website Calendar' checkbox, unchecked by default."),
            T("gig-archive-removes-from-calendar-only-if-checked", "Gig Management", "Archive checkbox is opt-in for the live site",
              "Archive a gig on a band with a connected website, leaving the checkbox UNCHECKED.",
              "Gig is archived in-app; the live site's calendar listing is untouched. Re-do it on a throwaway gig WITH the box checked and confirm the site listing is removed."),
            T("gig-unarchive-modal", "Gig Management", "Unarchiving a gig confirms and restores",
              "View Archived Gigs > pick one > confirm in the new Unarchive modal.",
              "Modal shows what will be restored, has its own 'add back to Website Calendar' checkbox (default unchecked), and the gig reappears in Upcoming/Past after confirming."),
            T("gig-setlist-build", "Gig Management", "Build a setlist from the repertoire",
              "Open a gig, add several songs from the band's repertoire to its set.",
              "Songs appear in order in the set; total duration calculates correctly; reordering persists."),
            T("gig-copy-setlist", "Gig Management", "Copy or assign a setlist from a past gig",
              "On a new gig, use 'Copy or Assign a Setlist' and pick a past gig's set.",
              "The past set's songs are copied in, editable independently afterward (editing the new gig's set doesn't change the original)."),
            T("gig-flyer-editor-basic", "Gig Management", "Create a flyer without publishing it live",
              "Create/Edit Flyer on a gig, make changes, Save WITHOUT the publish checkbox.",
              "Flyer saves to the catalog and is selectable, but nothing changes on the band's real public site."),
            T("gig-flyer-publish-checkbox", "Gig Management", "Publish checkbox actually pushes live",
              "Edit a flyer on a connected-site band, check the publish box, Save.",
              "The band's live site shows the updated flyer within the expected delay; unchecking next time does not re-push."),
            T("gig-select-flyer-live-badge", "Gig Management", "Select Flyer shows which one is live",
              "Create two flyers for one gig, open Select Flyer.",
              "Exactly one tile shows a 'Website Live' badge; choosing the other tile's checkbox switches which one is live on the real site."),
            T("gig-print-setlist", "Gig Management", "Print a setlist at a large font size",
              "Print Setlist on a gig with a full set, choose a large font size, generate.",
              "Header and first setlist line don't overlap at any chosen size."),
            T("gig-prep-per-gig", "Gig Management", "Gig Prep is private per member",
              "Two different band members open Gig Prep on the same gig and each add items.",
              "Each member sees only their own checklist items on that gig - not the other member's."),
            T("gig-accounting-view-vs-edit", "Gig Management", "Gig Accounting view vs edit permissions",
              "As a plain member, open a gig's Accounting view. As a Band Admin, edit the payout header/amounts on the same gig.",
              "The plain member can see amounts/paid-status read-only with no way to save; the Band Admin can edit and save both the header and per-recipient status."),
            T("gig-payout-notification", "Gig Management", "Marking someone paid notifies them",
              "As Band Admin, flip a recipient's paid status from unpaid to paid on a gig, save.",
              "That member gets a notification saying they were paid for that gig; flipping it back notifies them they have not yet been paid."),
            T("gig-tile-sort-order", "Gig Management", "Gig tiles stay chronological",
              "Add several gigs with different dates in non-chronological creation order, reload the page a few times.",
              "The tile order stays consistently sorted by date every time, never shuffling."),
        });

        // --- Band Flow > Calendar ---
        All.AddRange(new[]
        {
            T("calendar-shows-gigs-rehearsals", "Calendar", "Calendar reflects gigs and rehearsals",
              "Open Calendar for a band with upcoming gigs and rehearsals.",
              "Both appear on their correct dates; clicking one shows its details."),
            T("calendar-matches-gig-management", "Calendar", "Calendar and Gig Management always agree",
              "Edit a gig's date in Gig Management, then check Calendar (and vice versa).",
              "Both views immediately reflect the same date - there's no separate copy of the data to drift out of sync."),
        });

        // --- Band Flow > Venue Campaigns ---
        All.AddRange(new[]
        {
            T("venue-add-and-book", "Venue Campaigns", "Add a venue and track outreach",
              "Venue Campaigns > add a new venue, add a contact, log an outreach step.",
              "Venue appears in the band's venue book (also selectable from Add a Gig); outreach history is retained per venue."),
            T("venue-default-promoter", "Venue Campaigns", "Venue's default promoter carries to a new gig",
              "Set a Default Promoter on a venue. Add a new gig at that venue.",
              "The gig picks up that promoter automatically (still changeable per-gig)."),
        });

        // --- My <Band> ---
        All.AddRange(new[]
        {
            T("nav-my-band-section", "My <Band> (nav)", "My <band> nav section is member-visible and correctly named",
              "Log in as a plain (non-admin) member, switch bands.",
              "A sidebar section literally titled 'My <that band's name>' is visible (not admin-only) and its label updates immediately when you switch bands."),
            T("nav-repertoire-single-location", "My <Band> (nav)", "Repertoire only appears under My <band>",
              "As a Band Admin, scan the whole sidebar.",
              "Repertoire is listed exactly once, under My <band> - not duplicated under Band flow or Band admin."),
            T("repertoire-add-song", "My <Band> Repertoire", "Add a song to the band's repertoire",
              "Repertoire > Add a song, search the catalog or hand-enter one, add it.",
              "Song appears in the grid with status New; duration/key display correctly if known."),
            T("repertoire-status-filter", "My <Band> Repertoire", "Filter by New/In Progress/Ready",
              "Check only 'In Progress' in the repertoire filters.",
              "Only In Progress songs show; unchecking all filters shows every song again (matches the documented 'none checked = all' rule)."),
            T("repertoire-duration-total", "My <Band> Repertoire", "Footer totals duration across the filtered set",
              "Apply a filter, note the footer total; compare to summing the visible rows by hand.",
              "Footer total matches the filtered rows' summed duration, not the whole unfiltered repertoire."),
            T("my-accounting-readonly", "My <Band> Accounting", "My Accounting shows your own payouts, read-only",
              "As a member who's on the payout roster with at least one gig payout recorded, open My <band> > Accounting.",
              "Grid lists your own gigs with your computed amount and paid status; there is no way to edit anything from this page."),
            T("my-accounting-empty-state", "My <Band> Accounting", "Empty state for a member not on the roster",
              "As a member NOT on the payout roster, open My <band> > Accounting.",
              "Shows a clear empty grid ('no payout history yet') rather than an error."),
        });

        // --- Band Admin ---
        All.AddRange(new[]
        {
            T("band-admin-general-roles", "Band Admin > General", "Assign a Band Role to a member",
              "Band Admin > General > Band Roles > assign an instrument/role to a member.",
              "Saves; that member's Profile > Your roles reflects it read-only."),
            T("band-admin-add-member", "Band Admin > General", "Add an existing or new user as a band member",
              "Band Admin > General > add a member by existing username, and separately by creating a brand-new user.",
              "Both paths result in the person appearing in the band's member list with the chosen role."),
            T("band-admin-setup-web-presence", "Band Admin > Configure Web Presence", "Connect a platform credential",
              "Settings > pick a platform, enter its required credential fields per the on-screen instructions, save.",
              "Credential saves (encrypted at rest) and the platform shows as connected; Dashboard now includes that platform's tiles."),
            T("band-admin-cadence-rules", "Band Admin > Cadence", "Edit a posting cadence rule",
              "Cadence > adjust an existing rule's schedule or message template, save.",
              "New content scheduled after the change follows the updated cadence; already-scheduled items are unaffected unless regenerated."),
            T("band-admin-catalog-upload", "Band Admin > Images and Flyers", "Upload and label a catalog image",
              "Images and Flyers > upload a new image, add a label.",
              "Thumbnail generates; image is selectable from anywhere a catalog picker appears (flyer editor, gallery)."),
            T("band-accounting-receivables-defaults", "Band Admin > Band Accounting", "Set default gig/merch payees",
              "Band Admin > Band Accounting > pick default recipients for gig and merch payments, save.",
              "Saves; a brand-new gig's payout header pre-fills with these defaults where applicable."),
            T("band-accounting-percentage-100", "Band Admin > Band Accounting", "Percentage split must total exactly 100",
              "Set up 3 payout recipients with percentages that sum to 97. Try to save.",
              "Save stays disabled/blocked with a running 'remaining: 3%' indicator; only enables once the split totals exactly 100."),
            T("band-accounting-roster-change", "Band Admin > Band Accounting", "Removing someone from the roster",
              "Uncheck a member from the payout roster, save.",
              "That member no longer appears in the percentage grid or in any gig's payout recipients grid going forward."),
        });

        // --- Notifications ---
        All.AddRange(new[]
        {
            T("notifications-bell-count", "Notifications", "Unread count matches the bell",
              "Trigger a notification (e.g. get marked paid on a gig). Check the sidebar bell.",
              "Bell shows an unread count; opening Notifications and reading it clears that count."),
            T("notifications-mark-read-unread", "Notifications", "Mark read/unread individually and in bulk",
              "On the Notifications page, mark one read, one unread, then select several and bulk-mark.",
              "Each action updates immediately and persists after reload."),
        });

        // --- SuperAdmin ---
        All.AddRange(new[]
        {
            T("superadmin-all-bands-grid", "SuperAdmin", "All Bands grid shows every band with correct status",
              "SuperAdmin > All bands, with no filter checkboxes checked.",
              "Every onboarded, candidate, and stub band is listed except archived ones; Status column shows green Onboard / red Candidate / grey Archived correctly."),
            T("superadmin-archive-band-notes", "SuperAdmin", "Archiving a band requires notes",
              "SuperAdmin > All bands > Archive a band.",
              "A modal requires entering notes before Continue is enabled; Cancel leaves the band untouched."),
            T("superadmin-create-user", "SuperAdmin", "Create a new user with band memberships",
              "SuperAdmin > All users > create a user, assign them to one or more bands with roles.",
              "User is created with a temp password (MustChangePassword set) and appears correctly in each assigned band's member list."),
            T("superadmin-song-search-credentials", "SuperAdmin", "Configure and clear YouTube/Spotify search credentials",
              "SuperAdmin > Song search > save a YouTube API key, confirm badge flips to Configured. Remove it, confirm it flips back.",
              "Badge accurately reflects configured/not-configured state in both directions (see the known badge-refresh bug fix if this fails)."),
            T("superadmin-usps-credentials", "SuperAdmin", "Configure USPS address validation",
              "SuperAdmin > Address validation > save real USPS Consumer Key/Secret.",
              "Badge shows Configured; Profile's address validation now returns real suggestions instead of the 'not set up' note."),
            T("superadmin-google-maps-credentials", "SuperAdmin", "Configure Google Maps driving-distance key",
              "SuperAdmin > Driving distance (Google Maps) > save a real API key.",
              "Badge shows Configured; a newly-saved Travel trip with two real addresses now gets a real computed Distance instead of blank."),
            T("superadmin-branding", "SuperAdmin", "Update BandManager's own branding",
              "SuperAdmin > BandManager branding > change logo/background/favicon.",
              "Changes apply across the whole app immediately (all bands, not just one)."),
            T("superadmin-song-catalog-review", "SuperAdmin", "Approve/reject a pending song edit",
              "Have a member propose a song edit (via Repertoire). SuperAdmin > Song catalog review > approve one, reject another.",
              "Approved edit applies to the shared catalog Song; rejected edit is discarded and the proposer isn't left in limbo (no stuck Pending state)."),
            T("superadmin-flyer-fonts", "SuperAdmin", "Upload a custom flyer font",
              "SuperAdmin > Flyer fonts > upload a valid TTF/OTF font.",
              "Font appears immediately in the Flyer Editor's font list for every band, in a fresh navigation (not just the tab it was uploaded from)."),
            T("superadmin-bulk-song-import", "SuperAdmin", "Bulk-import songs via CSV",
              "SuperAdmin > Bulk-import songs > upload a CSV including a sharp/flat key like 'A♭' or 'F# minor', select target bands.",
              "Import succeeds, correctly parsing non-ASCII key notation; imported songs appear in each selected band's repertoire."),
        });
    }
}
