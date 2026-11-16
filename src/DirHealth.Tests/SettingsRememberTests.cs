using System.IO;
using System.Threading;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.Storage;
using DirHealth.Desktop.ViewModels;
using Xunit;

namespace DirHealth.Tests;

// M1 (review 2026-11-16): Settings "Save" must respect the "Remember credentials" choice.
// Every test uses its own temp credentials file — the real %APPDATA% credentials.dat is never touched.
public class SettingsRememberTests : IDisposable
{
    private readonly string _dir  = Path.Combine(Path.GetTempPath(), "dirhealth-m1-" + Guid.NewGuid().ToString("N"));
    private string CredPath => Path.Combine(_dir, "credentials.dat");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    // Save() starts a DispatcherTimer; run it on an STA thread like the UI does.
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var t = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException($"STA action failed: {error}");
    }

    private SettingsViewModel NewVm(AdConnector connector) => new(connector, CredPath);

    [Fact]
    public void NoStoredCredentials_RememberStartsOff()
    {
        var vm = NewVm(new AdConnector());
        Assert.False(vm.RememberCredentials);
    }

    [Fact]
    public void StoredCredentials_RememberStartsOn()
    {
        CredentialStore.Save(CredPath, "corp.local", "admin", "secret-1");
        var vm = NewVm(new AdConnector());
        Assert.True(vm.RememberCredentials);
    }

    [Fact]
    public void RememberOff_Save_PersistsNoPassword_ButUpdatesConnector()
    {
        var connector = new AdConnector();
        RunSta(() =>
        {
            var vm = NewVm(connector);
            vm.Domain = "corp.local"; vm.Username = "admin"; vm.Password = "secret-2";
            vm.RememberCredentials = false;
            vm.Save();
        });

        Assert.False(File.Exists(CredPath));
        Assert.Null(CredentialStore.Load(CredPath));
        Assert.Equal("corp.local", connector.Domain);
        Assert.Equal("admin", connector.Username);
        Assert.Equal("secret-2", connector.Password);
    }

    [Fact]
    public void RememberOff_Save_DeletesExistingCredentialsFile()
    {
        CredentialStore.Save(CredPath, "corp.local", "admin", "old-secret");
        RunSta(() =>
        {
            var vm = NewVm(new AdConnector());
            Assert.True(vm.RememberCredentials);   // file existed → loaded as on
            vm.RememberCredentials = false;         // user unticks in Settings
            vm.Password = "new-secret";
            vm.Save();
        });

        Assert.False(File.Exists(CredPath));
    }

    [Fact]
    public void RememberOn_Save_PersistsCredentials()
    {
        RunSta(() =>
        {
            var vm = NewVm(new AdConnector());
            vm.Domain = "corp.local"; vm.Username = "admin"; vm.Password = "secret-3";
            vm.RememberCredentials = true;
            vm.Save();
        });

        var loaded = CredentialStore.Load(CredPath);
        Assert.NotNull(loaded);
        Assert.Equal("corp.local", loaded.Domain);
        Assert.Equal("admin", loaded.Username);
        Assert.Equal("secret-3", loaded.Password);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Save_AlwaysInvokesCacheInvalidation(bool remember)
    {
        int calls = 0;
        RunSta(() =>
        {
            var vm = NewVm(new AdConnector());
            vm.OnCredentialsSaved = () => calls++;
            vm.Username = "admin"; vm.Password = "secret-4";
            vm.RememberCredentials = remember;
            vm.Save();
        });

        Assert.Equal(1, calls);
    }
}
