using System.IO;
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
            // No backup means there was no original file — clear what the test wrote
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
}
