namespace BandManager.Data.Entities;

public enum ExternalCalendarProvider { Google, Outlook }

/// <summary>
/// One user's OAuth connection to their own Google Calendar or Outlook
/// calendar - the per-user half of Phase 8's external sync (the other
/// half, the OAuth app's own Client ID/Secret, is a single global
/// SuperAdmin-managed credential - see SuperAdminController's
/// calendar-oauth-credentials endpoints - since Google/Microsoft only
/// need one registered app for this whole BandManager instance, not one
/// per user). EncryptedTokens is a JSON blob (access token, refresh
/// token, expiry) encrypted the same way CredentialStore encrypts a
/// Band's platform credentials - see ICredentialCipher.
/// </summary>
public class UserExternalCalendarConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public ExternalCalendarProvider Provider { get; set; }
    public required string EncryptedTokens { get; set; }
    public string? ExternalCalendarId { get; set; }
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
}
