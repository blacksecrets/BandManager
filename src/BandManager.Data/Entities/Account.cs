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
    public string? EncryptedCredentials { get; set; }

    public DateOnly? TokenExpiresAt { get; set; }
    public bool? LastVerifiedOk { get; set; }
    public string? LastVerificationError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
