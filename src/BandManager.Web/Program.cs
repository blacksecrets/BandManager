using BandManager.Data;
using BandManager.Data.Crypto;
using BandManager.Data.Entities;
using BandManager.Data.Seed;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

// Without this, ASP.NET Core generates a fresh in-memory key ring per
// process - every container restart silently invalidates every logged-in
// user's auth cookie (decrypts to garbage under the new key, so it's
// treated as "not logged in," not an error). Persisting it into the same
// data/ bind mount that already survives restarts (secret.key, uploads,
// etc.) fixes that: a rebuild/redeploy no longer logs anyone out.
builder.Services.AddDataProtection()
    .SetApplicationName("BandManager")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "data", "dpkeys")));

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        // 8+ chars, at least one uppercase, lowercase, digit, and special
        // character - RequireUppercase/Lowercase/Digit are already true by
        // default (never overridden here), so RequireNonAlphanumeric is
        // the only one that needs setting explicitly.
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequiredLength = 8;
        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddScoped<IUserClaimsPrincipalFactory<ApplicationUser>, ApplicationUserClaimsPrincipalFactory>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "BandManager.Auth";
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.LoginPath = "/login.html";
    options.Events.OnRedirectToLogin = context =>
    {
        // The frontend's fetch() calls expect a plain 401 to react to
        // (e.g. bounce to login client-side), not an HTML redirect body -
        // only actual page navigation should redirect.
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "BandManager.Session";
    options.IdleTimeout = TimeSpan.FromDays(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddScoped<IActiveBandAccessor, ActiveBandAccessor>();

// Forgot-password emails. LoggingEmailSender is a dev-mode stand-in (logs
// the reset link instead of sending it) - swap this registration for a
// real SMTP/SendGrid/etc. sender once one is chosen; nothing else changes.
builder.Services.AddScoped<BandManager.Web.Services.IEmailSender, BandManager.Web.Services.LoggingEmailSender>();
builder.Services.AddScoped<BandManager.Web.Services.UserProvisioningService>();
builder.Services.AddScoped<BandManager.Web.Services.SongSearchService>();
builder.Services.AddScoped<BandManager.Web.Services.NotificationReminderService>();
builder.Services.AddScoped<BandManager.Web.Services.AddressLookupService>();
builder.Services.AddScoped<BandManager.Web.Services.GoogleCalendarPushService>();
builder.Services.AddScoped<BandManager.Web.Services.OutlookCalendarPushService>();
builder.Services.AddScoped<BandManager.Web.Services.TechRiderPdfService>();

// The one genuinely irreplaceable piece of local state - see
// AesGcmCredentialCipher's doc comment. Configurable via
// CredentialKeyPath so a deployment can point it at a persistent volume;
// defaults to a data/ folder next to the app, same convention the old
// Node app used.
var keyPath = builder.Configuration["CredentialKeyPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "secret.key");
builder.Services.AddSingleton<ICredentialCipher>(new AesGcmCredentialCipher(keyPath));
builder.Services.AddScoped<BandManager.Data.Services.CredentialStore>();
builder.Services.AddScoped<BandManager.Data.Services.CalendarFeedService>();
builder.Services.AddScoped<BandManager.Data.Services.BandMembershipService>();
builder.Services.AddScoped<BandManager.Data.Services.CadenceAutoLinkService>();

var uploadsRootPath = Path.Combine(builder.Environment.ContentRootPath, "data", "uploads");
var catalogRootPath = Path.Combine(builder.Environment.ContentRootPath, "data", "catalog");
var flyerCacheRootPath = Path.Combine(builder.Environment.ContentRootPath, "data", "flyer-cache");
var bandBrandingRootPath = Path.Combine(builder.Environment.ContentRootPath, "data", "band-branding");
var customFontsRootPath = Path.Combine(builder.Environment.ContentRootPath, "data", "fonts");

builder.Services.AddHttpClient(); // generic IHttpClientFactory, for FlyerCache/CatalogStore below

builder.Services.AddScoped<BandManager.Data.Services.CatalogStore>(sp =>
    new BandManager.Data.Services.CatalogStore(sp.GetRequiredService<ApplicationDbContext>(), catalogRootPath, sp.GetRequiredService<IHttpClientFactory>().CreateClient()));

// Per-Band site content sourcing (calendar.js/media.js/gallery.js) - plain
// HttpClient-typed services, no extra constructor params, so a simple
// AddHttpClient<T>() registration is enough for these three.
builder.Services.AddHttpClient<BandManager.Data.Services.GigsSource>();
builder.Services.AddHttpClient<BandManager.Data.Services.MediaSource>();
builder.Services.AddHttpClient<BandManager.Data.Services.GallerySource>();
builder.Services.AddHttpClient<BandManager.Data.Services.GitHubSiteClient>();

builder.Services.AddScoped<BandManager.Data.Services.FlyerCache>(sp =>
    new BandManager.Data.Services.FlyerCache(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), flyerCacheRootPath));

// Registered by interface, not concrete type - see SitePublishing.cs.
// Controllers depend on IGigSitePublisher/IGallerySitePublisher/
// IMediaSitePublisher/ITechRiderSitePublisher/IBandSiteConnection so a
// future site format can be swapped in here later without touching any
// controller. The concrete *SiteEditor classes' own static helpers
// (GenerateThumbnail, ToEmbedUrl, PdfPath - pure computation, no site
// I/O) are still called by their concrete type name where needed.
builder.Services.AddScoped<BandManager.Data.Services.IBandSiteConnection, BandManager.Data.Services.BandSiteConnection>();
builder.Services.AddScoped<BandManager.Data.Services.IGigSitePublisher, BandManager.Data.Services.GigsSiteEditor>();
builder.Services.AddScoped<BandManager.Data.Services.ITechRiderSitePublisher, BandManager.Data.Services.TechRiderSiteEditor>();
builder.Services.AddScoped<BandManager.Data.Services.IMediaSitePublisher, BandManager.Data.Services.MediaSiteEditor>();
builder.Services.AddScoped<BandManager.Data.Services.IGallerySitePublisher, BandManager.Data.Services.GallerySiteEditor>();

builder.Services.AddScoped<BandManager.Data.Services.Scheduler>(sp =>
    new BandManager.Data.Services.Scheduler(
        sp.GetRequiredService<ApplicationDbContext>(),
        sp.GetRequiredService<BandManager.Data.Services.FlyerCache>(),
        sp.GetRequiredService<BandManager.Data.Services.CatalogStore>(),
        uploadsRootPath));

builder.Services.AddHostedService<BandManager.Web.CadenceGenerationBackgroundService>();
builder.Services.AddHttpClient<BandManager.Web.Publishers.FacebookPublisher>();
builder.Services.AddHttpClient<BandManager.Web.Publishers.InstagramPublisher>();
builder.Services.AddHttpClient<BandManager.Web.Publishers.GoogleBusinessPublisher>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("BandMember", policy => policy.Requirements.Add(new BandMemberRequirement()));
    options.AddPolicy("BandAdmin", policy => policy.Requirements.Add(new BandAdminRequirement()));
    options.AddPolicy("SuperAdmin", policy => policy.Requirements.Add(new SuperAdminRequirement()));
});
builder.Services.AddScoped<IAuthorizationHandler, BandMemberRequirementHandler>();
builder.Services.AddScoped<IAuthorizationHandler, BandAdminRequirementHandler>();
builder.Services.AddScoped<IAuthorizationHandler, SuperAdminRequirementHandler>();

var app = builder.Build();

// Dev/deploy convenience: apply pending EF Core migrations automatically
// on startup instead of a separate manual step - mirrors the old Node
// app's db.js running its schema/migrations inline on every server start.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
    await DbSeeder.SeedAsync(db);
}

app.UseDefaultFiles();
// no-cache (not no-store) - the browser still keeps a copy and can reuse
// it, but only after revalidating with the server on every request
// (a cheap conditional GET, 304 if unchanged). Without this, browsers
// apply their own heuristic freshness window to these files with no
// explicit Cache-Control header, which has repeatedly served a stale
// wwwroot/assets/*.js after a deploy even on a plain reload - a real bug
// hunted down more than once, not just a hygiene nicety.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});

// Public (no login required) - the login page itself needs to show
// branding before anyone's authenticated, same as the old app's
// publicRouter split for this exact route.
var brandingRootPath = Path.Combine(builder.Environment.ContentRootPath, "data", "branding");
Directory.CreateDirectory(brandingRootPath);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(brandingRootPath),
    RequestPath = "/branding"
});

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// Uploaded artifacts, only reachable once logged in - mirrors the old
// app's /uploads static mount, which sat after requireLogin for the same
// reason. Known gap (same as the old single-tenant app never had to
// consider): this checks "is authenticated," not "does this file belong
// to a Band you're a member of" - a logged-in user who somehow learned
// another Band's item GUID could fetch its file. Worth tightening if this
// stops being a small, trusted set of bands.
void MapAuthenticatedStaticFiles(string requestPath, string physicalPath)
{
    Directory.CreateDirectory(physicalPath);
    app.UseWhen(ctx => ctx.Request.Path.StartsWithSegments(requestPath), branch =>
    {
        branch.Use(async (ctx, next) =>
        {
            if (ctx.User.Identity?.IsAuthenticated != true)
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            await next();
        });
        branch.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(physicalPath),
            RequestPath = requestPath
        });
    });
}

MapAuthenticatedStaticFiles("/uploads", uploadsRootPath);
MapAuthenticatedStaticFiles("/band-branding", bandBrandingRootPath);
MapAuthenticatedStaticFiles("/custom-fonts", customFontsRootPath);
// Extensionless page routes - the reused frontend's nav links (topbar
// <a href="/settings">, etc., carried over unchanged from the old app)
// point at these, not the .html filenames directly. The .html files stay
// directly reachable too via the plain UseStaticFiles() above (a minor,
// disclosed gap: unlike the old app, that path doesn't require login -
// no secrets live in the page shells themselves, just markup/JS, but
// worth tightening later if that stops being true).
foreach (var page in new[] { "dashboard", "settings", "cadence", "catalog", "profile", "band-admin", "superadmin", "repertoire", "gig-sets", "notifications", "calendar", "print-setlist", "venue-campaigns", "print-gig-prep" })
{
    // Plain "logged in" (not the BandMember policy) - same as "/" (the
    // Dashboard, served unconditionally above) needs no active Band just
    // to *render*; each page's own JS shows a "no band selected" state
    // and its API calls enforce BandMember for real. Requiring the policy
    // here too would 403 a SuperAdmin who clicks Cadence/Setup before
    // ever using the band switcher, instead of letting the page load and
    // prompt them.
    app.MapGet($"/{page}", (HttpContext ctx) =>
            Results.File(Path.Combine(builder.Environment.WebRootPath, $"{page}.html"), "text/html"))
        .RequireAuthorization();
}
// Named /catalog-files, not /catalog, to avoid colliding with the
// CatalogController's own GET /api/catalog route (and the eventual
// GET /catalog page route) - same reasoning as the old app.
MapAuthenticatedStaticFiles("/catalog-files", catalogRootPath);

app.MapControllers();

app.Run();
