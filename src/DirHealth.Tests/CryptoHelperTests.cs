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
}
