using System.Security.Cryptography;
using System.Text;

namespace DirHealth.Desktop.Core.Crypto;

public static class CryptoHelper
{
    private const int KeySize   = 32; // AES-256
    private const int IvSize    = 16;
    private const int HmacSize  = 32;
    private const int SaltSize  = 16;
    private const int Iterations = 100_000;

    public static string Encrypt(string plaintext, string passphrase)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var iv   = RandomNumberGenerator.GetBytes(IvSize);
        var key  = DeriveKey(passphrase, salt);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV  = iv;

        using var enc = aes.CreateEncryptor();
        var data      = Encoding.UTF8.GetBytes(plaintext);
        var cipher    = enc.TransformFinalBlock(data, 0, data.Length);

        var payload = Combine(salt, iv, cipher);
        var hmac    = ComputeHmac(key, payload);

        return Convert.ToBase64String(Combine(hmac, payload));
    }

    public static string Decrypt(string ciphertext, string passphrase)
    {
        var blob    = Convert.FromBase64String(ciphertext);
        var hmac    = blob[..HmacSize];
        var payload = blob[HmacSize..];

        var salt   = payload[..SaltSize];
        var iv     = payload[SaltSize..(SaltSize + IvSize)];
        var cipher = payload[(SaltSize + IvSize)..];

        var key = DeriveKey(passphrase, salt);

        if (!CryptographicOperations.FixedTimeEquals(hmac, ComputeHmac(key, payload)))
            throw new CryptographicException("HMAC verification failed — data may be tampered.");

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV  = iv;

        using var dec = aes.CreateDecryptor();
        var plain = dec.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plain);
    }

    public static bool TryDecrypt(string ciphertext, string passphrase, out string plaintext)
    {
        try
        {
            plaintext = Decrypt(ciphertext, passphrase);
            return true;
        }
        catch
        {
            plaintext = string.Empty;
            return false;
        }
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt)
        => new Rfc2898DeriveBytes(passphrase, salt, Iterations, HashAlgorithmName.SHA256)
            .GetBytes(KeySize);

    private static byte[] ComputeHmac(byte[] key, byte[] data)
        => HMACSHA256.HashData(key, data);

    private static byte[] Combine(params byte[][] arrays)
    {
        var result = new byte[arrays.Sum(a => a.Length)];
        int offset = 0;
        foreach (var a in arrays) { a.CopyTo(result, offset); offset += a.Length; }
        return result;
    }
}
