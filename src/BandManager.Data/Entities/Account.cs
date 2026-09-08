namespace BandManager.Data.Entities;

/// <summary>
/// One Band's configured instance of a platform - credentials, encrypted
/// at rest (see ICredentialCipher), plus verification/expiry status.
/// Reused-but-separately-stored across Bands: the Setup screen's "copy
/// from another band" picker reads a source Band's Account here and
/// writes a brand new row under the current Band - never a shared/linked
/// row (same copy-not-reference principle as the Catalog).
/// </summary>
public class Account
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BandId { get; set; }
    public Band Band { get; set; } = null!;

    public required string PlatformId { get; set; }
    public Platform Platform { get; set; } = null!;

    public required string Label { get; set; }

    // "We post to this platform" - independent of whether it's actually
    // automated. Defaults false: PlatformsController.List lazily creates
    // a blank Account row for every platform a Band doesn't have one for
    // yet (so every platform always has a stable AccountId), and those
    // placeholders must start unonboarded - a brand new Band hasn't
    // chosen to use any platform yet, let alone all eight of them. The
    // one existing-data migration (AccountOnboarding) backfilled true
    // onto every row that already existed before this column did, since
    // those all represented a real, already-active connection - this
    // default only governs rows created from here on. Toggling this off
    // does NOT clear EncryptedCredentials - "we no longer post here" and
    // "disconnect the automation" are deliberately separate actions on
    // the same row (see CredentialStore.ClearCredentialAsync for the
    // latter).
    public bool IsOnboarded { get; set; }

    public string? EncryptedCredentials { get; set; }

    public DateOnly? TokenExpiresAt { get; set; }
    public bool? LastVerifiedOk { get; set; }
    public string? LastVerificationError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
