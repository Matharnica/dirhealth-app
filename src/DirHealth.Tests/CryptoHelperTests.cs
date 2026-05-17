using DirHealth.Desktop.Core.Crypto;
using Xunit;

namespace DirHealth.Tests;

public class CryptoHelperTests
{
    private const string Passphrase = "test-passphrase-32-chars-minimum!";

    [Fact]
    public void EncryptDecrypt_RoundTrip()
    {
        var plaintext  = "Hello, DirHealth!";
        var ciphertext = CryptoHelper.Encrypt(plaintext, Passphrase);
        var decrypted  = CryptoHelper.Decrypt(ciphertext, Passphrase);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextEachTime()
    {
        var plaintext = "same input";
        var c1 = CryptoHelper.Encrypt(plaintext, Passphrase);
        var c2 = CryptoHelper.Encrypt(plaintext, Passphrase);
        Assert.NotEqual(c1, c2); // different random salt/IV each time
    }

    [Fact]
    public void Decrypt_ThrowsOnTamperedData()
    {
        var ciphertext = CryptoHelper.Encrypt("secret", Passphrase);
        var bytes      = Convert.FromBase64String(ciphertext);
        bytes[10] ^= 0xFF; // flip bits
        var tampered   = Convert.ToBase64String(bytes);
        Assert.ThrowsAny<Exception>(() => CryptoHelper.Decrypt(tampered, Passphrase));
    }

    [Fact]
    public void TryDecrypt_ReturnsFalseOnInvalidData()
    {
        var result = CryptoHelper.TryDecrypt("not-valid-base64!!!", Passphrase, out var plain);
        Assert.False(result);
        Assert.Equal(string.Empty, plain);
    }

    [Fact]
    public void Decrypt_ThrowsWithWrongPassphrase()
    {
        var ciphertext = CryptoHelper.Encrypt("secret", Passphrase);
        Assert.ThrowsAny<Exception>(() => CryptoHelper.Decrypt(ciphertext, "wrong-passphrase"));
    }

    [Fact]
    public void Decrypt_ThrowsOnTruncatedBlob()
    {
        // Blob shorter than HmacSize(32) + SaltSize(16) + IvSize(16) = 64 bytes
        var tooShort = Convert.ToBase64String(new byte[10]);
        Assert.ThrowsAny<Exception>(() => CryptoHelper.Decrypt(tooShort, Passphrase));
    }

    [Fact]
    public void EncryptDecrypt_EmptyPlaintext_RoundTrip()
    {
        var ciphertext = CryptoHelper.Encrypt("", Passphrase);
        var decrypted  = CryptoHelper.Decrypt(ciphertext, Passphrase);
        Assert.Equal("", decrypted);
    }

    [Fact]
    public void Decrypt_ThrowsOnHmacRegionTampering()
    {
        var ciphertext = CryptoHelper.Encrypt("secret", Passphrase);
        var bytes = Convert.FromBase64String(ciphertext);
        bytes[0] ^= 0xFF; // flip bits in the HMAC region (first 32 bytes)
        Assert.ThrowsAny<Exception>(() => CryptoHelper.Decrypt(Convert.ToBase64String(bytes), Passphrase));
    }
}
