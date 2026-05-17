using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DirHealth.Desktop.Core.Crypto;
using DirHealth.Desktop.Core.HWID;
using DirHealth.Desktop.Core.Storage;
using Xunit;

namespace DirHealth.Tests;

public class CredentialStoreTests
{
    private static readonly string CredPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DirHealth", "credentials.dat");

    private static readonly string BackupPath = CredPath + ".test-bak";

    private static void Backup()
    {
        if (File.Exists(CredPath))
            File.Copy(CredPath, BackupPath, overwrite: true);
    }

    private static void Restore()
    {
        if (File.Exists(BackupPath))
        {
            File.Copy(BackupPath, CredPath, overwrite: true);
            File.Delete(BackupPath);
        }
        else
        {
            CredentialStore.Clear();
        }
    }

    [Fact]
    public void Save_AndLoad_RoundTrip()
    {
        Backup();
        try
        {
            CredentialStore.Save("corp.local", "CORP\\admin", "P@ssw0rd!");
            var loaded = CredentialStore.Load();
            Assert.NotNull(loaded);
            Assert.Equal("corp.local", loaded.Domain);
            Assert.Equal("CORP\\admin", loaded.Username);
            Assert.Equal("P@ssw0rd!", loaded.Password);
        }
        finally { Restore(); }
    }

    [Fact]
    public void Clear_AfterSave_LoadReturnsNull()
    {
        Backup();
        try
        {
            CredentialStore.Save("corp.local", "CORP\\admin", "P@ssw0rd!");
            CredentialStore.Clear();
            var loaded = CredentialStore.Load();
            Assert.Null(loaded);
        }
        finally { Restore(); }
    }

    [Fact]
    public void Load_WhenNoFileExists_ReturnsNull()
    {
        Backup();
        try
        {
            CredentialStore.Clear();
            var loaded = CredentialStore.Load();
            Assert.Null(loaded);
        }
        finally { Restore(); }
    }

    [Fact]
    public void Load_LegacyHwidFormat_MigratesCredentialsAndResavesAsDpapi()
    {
        Backup();
        try
        {
            // Write credentials in the legacy format (CryptoHelper + HWID passphrase stored as UTF-8 text)
            var creds          = new SavedCredentials("legacy.corp", "CORP\\migrated", "OldPass!");
            var json           = JsonSerializer.Serialize(creds);
            var passphrase     = HwidManager.ComputeHWID()[..16];
            var legacyBlob     = Encoding.UTF8.GetBytes(CryptoHelper.Encrypt(json, passphrase));
            Directory.CreateDirectory(Path.GetDirectoryName(CredPath)!);
            File.WriteAllBytes(CredPath, legacyBlob);

            // Load should decrypt via legacy path and return correct credentials
            var loaded = CredentialStore.Load();
            Assert.NotNull(loaded);
            Assert.Equal("legacy.corp", loaded.Domain);
            Assert.Equal("CORP\\migrated", loaded.Username);
            Assert.Equal("OldPass!", loaded.Password);

            // File should now be re-saved in DPAPI format (verifiable by ProtectedData.Unprotect)
            var raw      = File.ReadAllBytes(CredPath);
            var dpapi    = Encoding.UTF8.GetString(ProtectedData.Unprotect(raw, null, DataProtectionScope.CurrentUser));
            var migrated = JsonSerializer.Deserialize<SavedCredentials>(dpapi);
            Assert.Equal("legacy.corp", migrated!.Domain);
        }
        finally { Restore(); }
    }
}
