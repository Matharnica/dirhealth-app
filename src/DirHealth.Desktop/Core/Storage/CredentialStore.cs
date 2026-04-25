using System.IO;
using System.Text.Json;
using DirHealth.Desktop.Core.Crypto;
using DirHealth.Desktop.Core.HWID;

namespace DirHealth.Desktop.Core.Storage;

public record SavedCredentials(string Domain, string Username, string Password);

public static class CredentialStore
{
    private static readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DirHealth", "credentials.dat");

    private static string Passphrase => HwidManager.ComputeHWID()[..16];

    public static SavedCredentials? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var decrypted = CryptoHelper.Decrypt(File.ReadAllText(_path), Passphrase);
            return JsonSerializer.Deserialize<SavedCredentials>(decrypted);
        }
        catch { return null; }
    }

    public static void Save(string domain, string username, string password)
    {
        try
        {
            var json      = JsonSerializer.Serialize(new SavedCredentials(domain, username, password));
            var encrypted = CryptoHelper.Encrypt(json, Passphrase);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, encrypted);
        }
        catch { }
    }

    public static void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }
}
