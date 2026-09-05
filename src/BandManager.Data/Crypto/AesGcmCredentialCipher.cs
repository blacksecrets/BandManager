using System.Security.Cryptography;
using System.Text;

namespace BandManager.Data.Crypto;

/// <summary>
/// AES-256-GCM at rest, key loaded from (or created at) a local file -
/// ported from the old app's src/crypto.js (same nonce(12)+tag(16)+
/// ciphertext base64 layout, so it's the same scheme, just re-implemented).
/// The key file is the one genuinely irreplaceable piece of local state:
/// lose it and every stored credential (Facebook/Instagram/GBP/GitHub
/// tokens) becomes permanently undecryptable - back it up alongside your
/// Postgres dumps, never commit it to source control.
/// </summary>
public class AesGcmCredentialCipher : ICredentialCipher
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly byte[] _key;

    public AesGcmCredentialCipher(string keyFilePath)
    {
        _key = LoadOrCreateKey(keyFilePath);
    }

    private static byte[] LoadOrCreateKey(string keyFilePath)
    {
        if (File.Exists(keyFilePath))
        {
            return Convert.FromHexString(File.ReadAllText(keyFilePath).Trim());
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var dir = Path.GetDirectoryName(keyFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(keyFilePath, Convert.ToHexStringLower(key));
        return key;
    }

    public string Encrypt(string plainText)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using (var aesGcm = new AesGcm(_key, TagSize))
        {
            aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        var payload = new byte[NonceSize + TagSize + cipherBytes.Length];
        nonce.CopyTo(payload, 0);
        tag.CopyTo(payload, NonceSize);
        cipherBytes.CopyTo(payload, NonceSize + TagSize);
        return Convert.ToBase64String(payload);
    }

    public string Decrypt(string payloadBase64)
    {
        var payload = Convert.FromBase64String(payloadBase64);
        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipherBytes = payload.AsSpan(NonceSize + TagSize);
        var plainBytes = new byte[cipherBytes.Length];

        using (var aesGcm = new AesGcm(_key, TagSize))
        {
            aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);
        }
        return Encoding.UTF8.GetString(plainBytes);
    }
}
