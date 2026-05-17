using System.IO;
using System.Security.Cryptography;
using System.Text;
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

    public static SavedCredentials? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var raw = File.ReadAllBytes(_path);

            // Try DPAPI (current format)
            try
            {
                var json = Encoding.UTF8.GetString(
                    ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser));
                return JsonSerializer.Deserialize<SavedCredentials>(json);
            }
            catch (CryptographicException) { }

            // Migration path: try legacy HWID-based encryption, then re-save with DPAPI
            try
            {
                var legacyPassphrase = HwidManager.ComputeHWID()[..16];
                var json = CryptoHelper.Decrypt(Encoding.UTF8.GetString(raw), legacyPassphrase);
                var creds = JsonSerializer.Deserialize<SavedCredentials>(json);
                if (creds is not null)
                    Save(creds.Domain, creds.Username, creds.Password);
                return creds;
            }
            catch { }

            return null;
        }
        catch { return null; }
    }

    public static void Save(string domain, string username, string password)
    {
        try
        {
            var json      = JsonSerializer.Serialize(new SavedCredentials(domain, username, password));
            var plaintext = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllBytes(_path, encrypted);
        }
        catch (Exception ex)
        {
            try
            {
                var logPath = Path.Combine(Path.GetDirectoryName(_path)!, "dirhealth.log");
                File.AppendAllText(logPath,
                    $"{DateTime.Now:HH:mm:ss} CredentialStore.Save failed: {ex.GetType().Name}: {ex.Message}\n");
            }
            catch { }
        }
    }

    public static void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }
}
