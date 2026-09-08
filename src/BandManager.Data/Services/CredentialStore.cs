using System.Text.Json;
using BandManager.Data.Crypto;
using BandManager.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace BandManager.Data.Services;

/// <summary>
/// Band-scoped equivalent of the old app's credentialsStore.js - same
/// operations, now keyed by (BandId, PlatformId) instead of just
/// PlatformId, since every Band has its own independent Account per
/// platform (never shared, even when a value started life copied from
/// another Band the same user administers - see the Setup plan's
/// "copy not reference" principle).
/// </summary>
public class CredentialStore(ApplicationDbContext db, ICredentialCipher cipher)
{
    private async Task<Account?> FindAccountAsync(Guid bandId, string platformId) =>
        await db.Accounts.FirstOrDefaultAsync(a => a.BandId == bandId && a.PlatformId == platformId);

    private async Task<Account> GetOrCreateAccountAsync(Guid bandId, string platformId)
    {
        var account = await FindAccountAsync(bandId, platformId);
        if (account is not null) return account;

        var platform = await db.Platforms.FindAsync(platformId)
            ?? throw new InvalidOperationException($"Unknown platform: {platformId}");

        account = new Account { BandId = bandId, PlatformId = platformId, Label = platform.DisplayName };
        db.Accounts.Add(account);
        return account;
    }

    public async Task SetCredentialAsync(Guid bandId, string platformId, Dictionary<string, string> values)
    {
        var account = await GetOrCreateAccountAsync(bandId, platformId);
        account.EncryptedCredentials = cipher.Encrypt(JsonSerializer.Serialize(values));
        account.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<Dictionary<string, string>?> GetCredentialAsync(Guid bandId, string platformId)
    {
        var account = await FindAccountAsync(bandId, platformId);
        if (account?.EncryptedCredentials is null) return null;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(cipher.Decrypt(account.EncryptedCredentials));
    }

    public async Task<bool> HasCredentialAsync(Guid bandId, string platformId)
    {
        var account = await FindAccountAsync(bandId, platformId);
        return account?.EncryptedCredentials is not null;
    }

    /// <summary>Whether this Band posts to this platform at all - not
    /// whether it's automated (see Account.IsOnboarded's doc comment). No
    /// Account row at all counts as false, same as "never touched."</summary>
    public async Task<bool> IsOnboardedAsync(Guid bandId, string platformId)
    {
        var account = await FindAccountAsync(bandId, platformId);
        return account?.IsOnboarded ?? false;
    }

    /// <summary>Flips "we post here" - works for every platform, including
    /// ones with no CredentialFields at all (nothing to automate, but the
    /// Band can still say yes/no to using it). Never touches
    /// EncryptedCredentials - see Account.IsOnboarded's doc comment.</summary>
    public async Task SetOnboardedAsync(Guid bandId, string platformId, bool onboarded)
    {
        var account = await GetOrCreateAccountAsync(bandId, platformId);
        account.IsOnboarded = onboarded;
        account.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Resets a platform back to "not configured" for this Band -
    /// clears the saved credentials and whatever verification result was
    /// recorded (a stale "connection failed" from the old token would be
    /// misleading once there's nothing to fail with).</summary>
    public async Task ClearCredentialAsync(Guid bandId, string platformId)
    {
        var account = await FindAccountAsync(bandId, platformId);
        if (account is null) return;
        account.EncryptedCredentials = null;
        account.LastVerifiedOk = null;
        account.LastVerificationError = null;
        account.TokenExpiresAt = null;
        account.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task SetTokenExpiryAsync(Guid bandId, string platformId, DateOnly? expiresAt)
    {
        var account = await GetOrCreateAccountAsync(bandId, platformId);
        account.TokenExpiresAt = expiresAt;
        account.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    public async Task<List<Account>> ListConfiguredAsync(Guid bandId) =>
        await db.Accounts.Where(a => a.BandId == bandId && a.EncryptedCredentials != null).ToListAsync();
}
