namespace BandManager.Data.Crypto;

public interface ICredentialCipher
{
    string Encrypt(string plainText);
    string Decrypt(string payloadBase64);
}
