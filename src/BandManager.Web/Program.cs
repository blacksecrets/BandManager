using BandManager.Data;
using BandManager.Data.Crypto;
using BandManager.Data.Entities;
using BandManager.Data.Seed;
using BandManager.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        // Internal tool, small trusted user base - relaxed but not absent.
        options.Password.RequireNonAlphanumeric = false;
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

// The one genuinely irreplaceable piece of local state - see
// AesGcmCredentialCipher's doc comment. Configurable via
// CredentialKeyPath so a deployment can point it at a persistent volume;
// defaults to a data/ folder next to the app, same convention the old
// Node app used.
var keyPath = builder.Configuration["CredentialKeyPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data", "secret.key");
builder.Services.AddSingleton<ICredentialCipher>(new AesGcmCredentialCipher(keyPath));
builder.Services.AddScoped<BandManager.Data.Services.CredentialStore>();
builder.Services.AddScoped<BandManager.Data.Services.BandMembershipService>();

var uploadsRootPath = Path.Combine(builder.Environment.ContentRootPath, "data", "uploads");
var catalogRootPath = Path.Combine(builder.Environment.ContentRootPath, "data", "catalog");
var flyerCacheRootPath = Path.Combine(builder.Environment.ContentRootPath, "data", "flyer-cache");

builder.Services.AddScoped<BandManager.Data.Services.CatalogStore>(sp =>
    new BandManager.Data.Services.CatalogStore(sp.GetRequiredService<ApplicationDbContext>(), catalogRootPath));

// Per-Band site content sourcing (calendar.js/media.js/gallery.js) - plain
// HttpClient-typed services, no extra constructor params, so a simple
// AddHttpClient<T>() registration is enough for these three.
builder.Services.AddHttpClient<BandManager.Data.Services.GigsSource>();
builder.Services.AddHttpClient<BandManager.Data.Services.MediaSource>();
builder.Services.AddHttpClient<BandManager.Data.Services.GallerySource>();

builder.Services.AddHttpClient(); // generic IHttpClientFactory, for FlyerCache below
builder.Services.AddScoped<BandManager.Data.Services.FlyerCache>(sp =>
    new BandManager.Data.Services.FlyerCache(sp.GetRequiredService<IHttpClientFactory>().CreateClient(), flyerCacheRootPath));

builder.Services.AddScoped<BandManager.Data.Services.Scheduler>(sp =>
    new BandManager.Data.Services.Scheduler(
        sp.GetRequiredService<ApplicationDbContext>(),
        sp.GetRequiredService<BandManager.Data.Services.GigsSource>(),
        sp.GetRequiredService<BandManager.Data.Services.MediaSource>(),
        sp.GetRequiredService<BandManager.Data.Services.GallerySource>(),
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
app.UseStaticFiles();

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
// Extensionless page routes - the reused frontend's nav links (topbar
// <a href="/settings">, etc., carried over unchanged from the old app)
// point at these, not the .html filenames directly. The .html files stay
// directly reachable too via the plain UseStaticFiles() above (a minor,
// disclosed gap: unlike the old app, that path doesn't require login -
// no secrets live in the page shells themselves, just markup/JS, but
// worth tightening later if that stops being true).
foreach (var page in new[] { "settings", "cadence", "catalog", "profile" })
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
