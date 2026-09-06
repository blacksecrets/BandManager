using System.Text.Json;
using BandManager.Data;
using BandManager.Data.Crypto;
using BandManager.Data.Entities;
using BandManager.Data.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

// One-time cutover tool: reads the old bs-crew-dashboard's SQLite database
// (single-tenant) and writes it into BandManager's Postgres schema as one
// new Band, carrying files (uploads/catalog) and re-encrypting credentials
// under BandManager's own key file. Every old numeric id is remapped to a
// fresh Guid via the *Map dictionaries below - nothing here reuses an old
// id as a new one. Safe to re-run against a fresh target Band (a new slug)
// but NOT idempotent against the same target - it always inserts, so
// running it twice against the same Band doubles everything.

var args_ = ParseArgs(args);
string Require(string name) => args_.TryGetValue(name, out var v) ? v : throw new ArgumentException($"Missing required --{name}");
string Optional(string name, string fallback) => args_.TryGetValue(name, out var v) ? v : fallback;

// Separate one-off mode, unrelated to the SQLite cutover this tool exists
// for otherwise - creating a SuperAdmin has no UI/API path of its own yet
// (SuperAdminController's own endpoints correctly require already being
// one), so this reaches the DB directly instead, the same way the cutover
// flow below already does. Exits immediately either way - never falls
// through to the migration-specific required args.
if (args_.ContainsKey("create-superadmin"))
{
    var email = Require("email");
    var password = Require("password");
    await using var db2 = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Require("connection")).Options);
    if (await db2.Users.AnyAsync(u => u.NormalizedUserName == email.ToUpperInvariant()))
        throw new InvalidOperationException($"A user named '{email}' already exists.");

    var newAdmin = new ApplicationUser
    {
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true,
        IsSuperAdmin = true,
        MustChangePassword = true,
        SecurityStamp = Guid.NewGuid().ToString(),
        ConcurrencyStamp = Guid.NewGuid().ToString(),
    };
    newAdmin.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(newAdmin, password);
    db2.Users.Add(newAdmin);
    await db2.SaveChangesAsync();
    Console.WriteLine($"Created SuperAdmin '{email}' ({newAdmin.Id}).");
    return;
}

var sqlitePath = Require("sqlite");
var oldRoot = Require("old-root"); // old app root - file_path columns are relative to this
var oldKeyPath = Require("old-key");
var bandName = Require("band-name");
var bandSlug = Require("band-slug");
var connectionString = Require("connection");
var newDataRoot = Require("new-data-root"); // BandManager Web's ContentRootPath/data
var newKeyPath = Require("new-key");
var tempPassword = Optional("temp-password", "Temp-" + Guid.NewGuid().ToString("N")[..12]);

if (!File.Exists(sqlitePath)) throw new FileNotFoundException("SQLite DB not found", sqlitePath);
if (!Directory.Exists(oldRoot)) throw new DirectoryNotFoundException($"old-root not found: {oldRoot}");

var uploadsRoot = Path.Combine(newDataRoot, "uploads");
var catalogRoot = Path.Combine(newDataRoot, "catalog");
Directory.CreateDirectory(uploadsRoot);
Directory.CreateDirectory(catalogRoot);

var oldCipher = new AesGcmCredentialCipher(oldKeyPath);
var newCipher = new AesGcmCredentialCipher(newKeyPath);

var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;
await using var db = new ApplicationDbContext(options);
await db.Database.MigrateAsync();
await DbSeeder.SeedAsync(db);

if (await db.Bands.AnyAsync(b => b.Slug == bandSlug))
    throw new InvalidOperationException($"A band with slug '{bandSlug}' already exists - pick a different --band-slug or remove it first.");

await using var sqlite = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly");
await sqlite.OpenAsync();

Console.WriteLine($"Importing '{bandName}' ({bandSlug}) from {sqlitePath} ...");

var band = new Band { Name = bandName, Slug = bandSlug };
db.Bands.Add(band);
await db.SaveChangesAsync();

// --- users -> ApplicationUser + BandMembership ---
// Old passwords are bcrypt hashes (bcryptjs), not compatible with ASP.NET
// Identity's PBKDF2 format - rather than shim a bcrypt verifier into the
// production auth stack for a one-time cutover, every migrated user gets
// the same temporary password (printed below) and should change it via
// Profile immediately after first login.
var hasher = new PasswordHasher<ApplicationUser>();
var userMap = new Dictionary<long, Guid>();
await foreach (var row in QueryAsync(sqlite, "SELECT id, username, is_admin FROM users"))
{
    var oldId = row.GetInt64(0);
    var username = row.GetString(1);
    var isAdmin = row.GetInt64(2) != 0;

    var user = new ApplicationUser
    {
        UserName = username,
        NormalizedUserName = username.ToUpperInvariant(),
        SecurityStamp = Guid.NewGuid().ToString(),
        ConcurrencyStamp = Guid.NewGuid().ToString(),
    };
    user.PasswordHash = hasher.HashPassword(user, tempPassword);
    db.Users.Add(user);
    await db.SaveChangesAsync();

    db.BandMemberships.Add(new BandMembership
    {
        UserId = user.Id,
        BandId = band.Id,
        Role = isAdmin ? BandRole.BandAdmin : BandRole.User
    });
    userMap[oldId] = user.Id;
    Console.WriteLine($"  user '{username}' -> temp password: {tempPassword}");
}
await db.SaveChangesAsync();

// --- accounts -> Account (re-encrypted under the new key) ---
var accountMap = new Dictionary<long, Guid>();
await foreach (var row in QueryAsync(sqlite,
    "SELECT id, platform_id, label, encrypted_credentials, token_expires_at, last_verified_ok, last_verification_error, created_at, updated_at FROM accounts"))
{
    var oldId = row.GetInt64(0);
    var platformId = row.GetString(1);
    if (await db.Platforms.FindAsync(platformId) is null)
    {
        Console.WriteLine($"  WARNING: skipping account for unknown platform '{platformId}'");
        continue;
    }

    string? encryptedCredentials = null;
    if (!row.IsDBNull(3))
    {
        var oldEncrypted = row.GetString(3);
        var plainJson = oldCipher.Decrypt(oldEncrypted);
        encryptedCredentials = newCipher.Encrypt(plainJson);
    }

    var account = new Account
    {
        BandId = band.Id,
        PlatformId = platformId,
        Label = row.GetString(2),
        EncryptedCredentials = encryptedCredentials,
        TokenExpiresAt = GetDateOnlyOrNull(row, 4),
        LastVerifiedOk = row.IsDBNull(5) ? null : row.GetInt64(5) != 0,
        LastVerificationError = row.IsDBNull(6) ? null : row.GetString(6),
        CreatedAt = ParseUtc(row.GetString(7)),
        UpdatedAt = ParseUtc(row.GetString(8)),
    };
    db.Accounts.Add(account);
    await db.SaveChangesAsync();
    accountMap[oldId] = account.Id;
}
Console.WriteLine($"  {accountMap.Count} account(s) migrated.");

// --- cadence_rules ---
var cadenceCount = 0;
await foreach (var row in QueryAsync(sqlite,
    "SELECT account_id, content_type_id, kind, category, description, owner, schedule_type, schedule_days, message_templates, manual_instructions, active, created_at, updated_at FROM cadence_rules"))
{
    var oldAccountId = row.GetInt64(0);
    if (!accountMap.TryGetValue(oldAccountId, out var newAccountId))
    {
        Console.WriteLine($"  WARNING: skipping cadence_rule - account {oldAccountId} was not migrated");
        continue;
    }

    db.CadenceRules.Add(new CadenceRule
    {
        AccountId = newAccountId,
        ContentTypeId = row.GetString(1),
        Kind = ParseCadenceKind(row.GetString(2)),
        Category = row.GetString(3),
        Description = row.GetString(4),
        Owner = row.IsDBNull(5) ? null : row.GetString(5),
        ScheduleType = row.IsDBNull(6) ? null : ParseScheduleType(row.GetString(6)),
        ScheduleDays = row.IsDBNull(7) ? null : ParseScheduleDays(row.GetString(7)),
        MessageTemplates = row.IsDBNull(8) ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(row.GetString(8)),
        ManualInstructions = row.IsDBNull(9) ? null : row.GetString(9),
        Active = row.GetInt64(10) != 0,
        CreatedAt = ParseUtc(row.GetString(11)),
        UpdatedAt = ParseUtc(row.GetString(12)),
    });
    cadenceCount++;
}
await db.SaveChangesAsync();
Console.WriteLine($"  {cadenceCount} cadence rule(s) migrated.");

// --- catalog_items (copies files into data/catalog/{bandId}/...) ---
var catalogMap = new Dictionary<long, Guid>();
var newCatalogBandDir = Path.Combine(catalogRoot, band.Id.ToString());
var newCatalogThumbsDir = Path.Combine(newCatalogBandDir, "thumbs");
Directory.CreateDirectory(newCatalogBandDir);
Directory.CreateDirectory(newCatalogThumbsDir);

await foreach (var row in QueryAsync(sqlite,
    "SELECT id, media_type, file_path, thumbnail_path, original_filename, label, mime_type, file_size, width, height, source, source_url, uploaded_by, created_at FROM catalog_items"))
{
    var oldId = row.GetInt64(0);
    var oldFilePath = row.GetString(2);
    var oldFullPath = Path.Combine(oldRoot, oldFilePath.Replace('/', Path.DirectorySeparatorChar));
    if (!File.Exists(oldFullPath))
    {
        Console.WriteLine($"  WARNING: skipping catalog_item {oldId} - file not found: {oldFullPath}");
        continue;
    }

    var fileName = Path.GetFileName(oldFullPath);
    var newFullPath = Path.Combine(newCatalogBandDir, fileName);
    File.Copy(oldFullPath, newFullPath, overwrite: true);
    var newRelPath = $"data/catalog/{band.Id}/{fileName}";

    string? newThumbRelPath = null;
    if (!row.IsDBNull(3))
    {
        var oldThumbFull = Path.Combine(oldRoot, row.GetString(3).Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(oldThumbFull))
        {
            var thumbName = Path.GetFileName(oldThumbFull);
            File.Copy(oldThumbFull, Path.Combine(newCatalogThumbsDir, thumbName), overwrite: true);
            newThumbRelPath = $"data/catalog/{band.Id}/thumbs/{thumbName}";
        }
    }

    var item = new CatalogItem
    {
        BandId = band.Id,
        MediaType = ParseMediaType(row.GetString(1)),
        FilePath = newRelPath,
        ThumbnailPath = newThumbRelPath,
        OriginalFilename = row.IsDBNull(4) ? null : row.GetString(4),
        Label = row.IsDBNull(5) ? null : row.GetString(5),
        MimeType = row.GetString(6),
        FileSize = row.GetInt64(7),
        Width = row.IsDBNull(8) ? null : (int)row.GetInt64(8),
        Height = row.IsDBNull(9) ? null : (int)row.GetInt64(9),
        Source = ParseCatalogSource(row.GetString(10)),
        SourceUrl = row.IsDBNull(11) ? null : row.GetString(11),
        UploadedBy = row.IsDBNull(12) ? null : row.GetString(12),
        CreatedAt = ParseUtc(row.GetString(13)),
    };
    db.CatalogItems.Add(item);
    await db.SaveChangesAsync();
    catalogMap[oldId] = item.Id;
}
Console.WriteLine($"  {catalogMap.Count} catalog item(s) migrated.");

// --- schedule_items ---
var scheduleMap = new Dictionary<long, Guid>();
await foreach (var row in QueryAsync(sqlite,
    "SELECT id, template_key, platform, owner, content_type, category, example, due_date, no_api, auto_handled, gig_ref, status, artifacts_owed, posted_at, posted_via, created_at, account_id, completed_at, media_ref, gallery_ref FROM schedule_items"))
{
    var oldId = row.GetInt64(0);
    Guid? newAccountId = null;
    if (!row.IsDBNull(16) && accountMap.TryGetValue(row.GetInt64(16), out var mapped)) newAccountId = mapped;

    var item = new ScheduleItem
    {
        BandId = band.Id,
        AccountId = newAccountId,
        TemplateKey = row.GetString(1),
        Platform = row.GetString(2),
        Owner = row.GetString(3),
        ContentType = row.GetString(4),
        Category = row.GetString(5),
        Example = row.IsDBNull(6) ? null : row.GetString(6),
        DueDate = GetDateOnlyOrNull(row, 7),
        NoApi = row.GetInt64(8) != 0,
        AutoHandled = row.GetInt64(9) != 0,
        GigRef = row.IsDBNull(10) ? null : row.GetString(10),
        Status = ParseScheduleItemStatus(row.GetString(11)),
        ArtifactsOwed = row.GetInt64(12) != 0,
        PostedAt = GetUtcOrNull(row, 13),
        PostedVia = row.IsDBNull(14) ? null : row.GetString(14),
        CreatedAt = ParseUtc(row.GetString(15)),
        CompletedAt = GetUtcOrNull(row, 17),
        MediaRef = row.IsDBNull(18) ? null : row.GetString(18),
        GalleryRef = row.IsDBNull(19) ? null : row.GetString(19),
    };
    db.ScheduleItems.Add(item);
    await db.SaveChangesAsync();
    scheduleMap[oldId] = item.Id;
}
Console.WriteLine($"  {scheduleMap.Count} schedule item(s) migrated.");

// --- artifacts (copies files into data/uploads/{newScheduleItemId}/...) ---
var artifactCount = 0;
await foreach (var row in QueryAsync(sqlite,
    "SELECT schedule_item_id, artifact_type, file_path, text_value, uploaded_by, uploaded_at FROM artifacts"))
{
    var oldScheduleItemId = row.GetInt64(0);
    if (!scheduleMap.TryGetValue(oldScheduleItemId, out var newScheduleItemId))
    {
        Console.WriteLine($"  WARNING: skipping artifact - schedule_item {oldScheduleItemId} was not migrated");
        continue;
    }

    string? newFilePath = null;
    if (!row.IsDBNull(2))
    {
        var oldFilePath = row.GetString(2);
        var oldFullPath = Path.Combine(oldRoot, oldFilePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(oldFullPath))
        {
            var destDir = Path.Combine(uploadsRoot, newScheduleItemId.ToString());
            Directory.CreateDirectory(destDir);
            var fileName = Path.GetFileName(oldFullPath);
            File.Copy(oldFullPath, Path.Combine(destDir, fileName), overwrite: true);
            newFilePath = $"data/uploads/{newScheduleItemId}/{fileName}";
        }
        else
        {
            Console.WriteLine($"  WARNING: artifact file not found, keeping row without a file: {oldFullPath}");
        }
    }

    db.Artifacts.Add(new Artifact
    {
        ScheduleItemId = newScheduleItemId,
        ArtifactType = row.GetString(1),
        FilePath = newFilePath,
        TextValue = row.IsDBNull(3) ? null : row.GetString(3),
        UploadedBy = row.IsDBNull(4) ? null : row.GetString(4),
        UploadedAt = ParseUtc(row.GetString(5)),
    });
    artifactCount++;
}
await db.SaveChangesAsync();
Console.WriteLine($"  {artifactCount} artifact(s) migrated.");

Console.WriteLine($"Done. Band '{bandName}' ({band.Id}) is ready. Migrated users must change their temporary password via Profile after first login.");

// The old SQLite db stores every timestamp via datetime('now'), which is
// always UTC but comes back from ADO.NET as Kind=Unspecified - Npgsql's
// "timestamp with time zone" columns reject anything but Kind=Utc.
static DateTime ParseUtc(string s) => DateTime.SpecifyKind(DateTime.Parse(s), DateTimeKind.Utc);

// Several old "nullable" TEXT columns (due_date in particular) hold '' as
// their empty state rather than a real SQL NULL - both mean "not set" here.
static DateOnly? GetDateOnlyOrNull(SqliteDataReader row, int i) =>
    row.IsDBNull(i) || row.GetString(i).Length == 0 ? null : DateOnly.Parse(row.GetString(i));
static DateTime? GetUtcOrNull(SqliteDataReader row, int i) =>
    row.IsDBNull(i) || row.GetString(i).Length == 0 ? null : ParseUtc(row.GetString(i));

// The old schema's schedule_days JSON mixes weekday name strings
// ("Monday") with day-of-month numbers (1, 15) and "end-of-month" -
// CadenceRule.ScheduleDays is List<string>, so every element gets
// normalized to its string form here regardless of its original JSON kind.
static List<string> ParseScheduleDays(string json) =>
    JsonSerializer.Deserialize<List<JsonElement>>(json)!
        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText())
        .ToList();

static async IAsyncEnumerable<SqliteDataReader> QueryAsync(SqliteConnection conn, string sql)
{
    await using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        yield return reader;
}

static Dictionary<string, string> ParseArgs(string[] argv)
{
    var result = new Dictionary<string, string>();
    for (var i = 0; i < argv.Length; i++)
    {
        if (!argv[i].StartsWith("--")) continue;
        var name = argv[i][2..];
        var value = i + 1 < argv.Length ? argv[i + 1] : throw new ArgumentException($"--{name} needs a value");
        result[name] = value;
        i++;
    }
    return result;
}

static CadenceKind ParseCadenceKind(string s) => s switch
{
    "recurring" => CadenceKind.Recurring,
    "gig_countdown" => CadenceKind.GigCountdown,
    "gig_event" => CadenceKind.GigEvent,
    "gig_cover_photo" => CadenceKind.GigCoverPhoto,
    _ => throw new InvalidOperationException($"Unknown cadence kind: {s}")
};

static ScheduleType ParseScheduleType(string s) => s switch
{
    "weekly" => ScheduleType.Weekly,
    "monthly" => ScheduleType.Monthly,
    _ => throw new InvalidOperationException($"Unknown schedule type: {s}")
};

static MediaType ParseMediaType(string s) => s switch
{
    "image" => MediaType.Image,
    "video" => MediaType.Video,
    "audio" => MediaType.Audio,
    _ => throw new InvalidOperationException($"Unknown media type: {s}")
};

static CatalogSource ParseCatalogSource(string s) => s switch
{
    "upload" => CatalogSource.Upload,
    "url" => CatalogSource.Url,
    "frame-capture" => CatalogSource.FrameCapture,
    "trim" => CatalogSource.Trim,
    "split" => CatalogSource.Split,
    "cover-photo" => CatalogSource.CoverPhoto,
    _ => throw new InvalidOperationException($"Unknown catalog source: {s}")
};

static ScheduleItemStatus ParseScheduleItemStatus(string s) => s switch
{
    "open" => ScheduleItemStatus.Open,
    "posted" => ScheduleItemStatus.Posted,
    "cancelled" => ScheduleItemStatus.Cancelled,
    _ => throw new InvalidOperationException($"Unknown schedule item status: {s}")
};
