using System.Runtime.InteropServices;
using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels;
using Xunit;

namespace DirHealth.Tests;

/// <summary>
/// Fix H1 (review 2026-11-16): WMI/ping only on click, only via dNSHostName, Kerberos authority,
/// errors visible. The fake records every host it was asked for — any call counts as network.
/// </summary>
public class WmiOnDemandTests
{
    private sealed class FakeWmi : IWmiClient
    {
        public List<string> Calls { get; } = new();
        public long PingResult { get; set; } = 3;
        public Exception? Throw { get; set; }

        private Task<List<T>> Query<T>(string what, string host, T item)
        {
            Calls.Add($"{what}:{host}");
            if (Throw is not null) return Task.FromException<List<T>>(Throw);
            return Task.FromResult(new List<T> { item });
        }

        public Task<long> PingTimeMs(string fqdn) { Calls.Add($"ping:{fqdn}"); return Task.FromResult(PingResult); }
        public Task<List<WmiDisk>> GetDisksAsync(string fqdn) => Query("disks", fqdn, new WmiDisk { Drive = "C:" });
        public Task<List<WmiLocalAdmin>> GetLocalAdminsAsync(string fqdn) => Query("admins", fqdn, new WmiLocalAdmin { Name = "a" });
        public Task<List<WmiLoggedOnUser>> GetLoggedOnUsersAsync(string fqdn) => Query("sessions", fqdn, new WmiLoggedOnUser { Name = "u" });
        public Task<List<WmiEventLogEntry>> GetEventLogAsync(string fqdn, string logName, string? severityFilter, int maxEntries)
            => Query("eventlog", fqdn, new WmiEventLogEntry { Source = "s" });
    }

    private static AdComputer Pc(string dns = "pc1.corp.example.com") =>
        new() { Name = "PC1", DnsHostName = dns, DistinguishedName = "CN=PC1,OU=C,DC=corp,DC=example,DC=com" };

    // ── Selection makes no connection ───────────────────────────────────────

    [Fact]
    public async Task Select_MakesNoPingOrWmiCall()
    {
        var wmi = new FakeWmi();
        var vm  = new ComputerDetailViewModel(wmi);
        await vm.LoadAsync(Pc());

        Assert.Empty(wmi.Calls);
        Assert.False(vm.LiveLoaded);
        Assert.Equal("", vm.WmiError);
        Assert.True(vm.LoadLiveDetailsCommand.CanExecute(null));
        Assert.False(vm.RefreshEventLogCommand.CanExecute(null));
    }

    [Fact]
    public async Task RefreshEventLog_BeforeLiveLoad_MakesNoCall()
    {
        var wmi = new FakeWmi();
        var vm  = new ComputerDetailViewModel(wmi);
        await vm.LoadAsync(Pc());
        await vm.RefreshEventLogAsync();
        Assert.Empty(wmi.Calls);
    }

    // ── Button uses the FQDN, never the cn ──────────────────────────────────

    [Fact]
    public async Task LoadLive_UsesFqdnForEveryCall_NeverCn()
    {
        var wmi = new FakeWmi();
        var vm  = new ComputerDetailViewModel(wmi);
        await vm.LoadAsync(Pc());
        await vm.LoadLiveDetailsAsync();

        Assert.Equal(
            new[] { "ping:pc1.corp.example.com", "disks:pc1.corp.example.com", "admins:pc1.corp.example.com",
                    "sessions:pc1.corp.example.com", "eventlog:pc1.corp.example.com" },
            wmi.Calls);
        Assert.DoesNotContain(wmi.Calls, c => c.EndsWith(":PC1", StringComparison.OrdinalIgnoreCase));
        Assert.True(vm.LiveLoaded);
        Assert.Single(vm.Disks);
        Assert.Single(vm.EventLog);
        Assert.Equal("", vm.WmiError);
        Assert.True(vm.RefreshEventLogCommand.CanExecute(null));

        await vm.RefreshEventLogAsync();
        Assert.Equal("eventlog:pc1.corp.example.com", wmi.Calls[^1]);
    }

    [Fact]
    public async Task SelectingAnotherComputer_DropsLiveData()
    {
        var wmi = new FakeWmi();
        var vm  = new ComputerDetailViewModel(wmi);
        await vm.LoadAsync(Pc());
        await vm.LoadLiveDetailsAsync();
        Assert.NotEmpty(vm.Disks);

        await vm.LoadAsync(Pc("pc2.corp.example.com"));
        Assert.Empty(vm.Disks); Assert.Empty(vm.LocalAdmins); Assert.Empty(vm.LoggedOn); Assert.Empty(vm.EventLog);
        Assert.False(vm.LiveLoaded);
        Assert.False(vm.IsOnline);
        Assert.False(vm.RefreshEventLogCommand.CanExecute(null));
    }

    // ── Missing dNSHostName: no connection, clear message ───────────────────

    [Theory]
    [InlineData("")]
    [InlineData("PC1")]                 // short name — would go to LLMNR/NetBIOS
    [InlineData("attacker\\share")]
    public async Task MissingOrInvalidDnsHostName_NoCalls_AndMessage(string dns)
    {
        var wmi = new FakeWmi();
        var vm  = new ComputerDetailViewModel(wmi);
        await vm.LoadAsync(Pc(dns));

        Assert.Equal(ComputerDetailViewModel.NoDnsHostNameMessage, vm.WmiError);
        Assert.False(vm.LoadLiveDetailsCommand.CanExecute(null));

        await vm.LoadLiveDetailsAsync();   // even if called directly
        Assert.Empty(wmi.Calls);
        Assert.Equal(ComputerDetailViewModel.NoDnsHostNameMessage, vm.WmiError);
    }

    [Fact]
    public async Task Offline_NoWmiCall_AndMessage()
    {
        var wmi = new FakeWmi { PingResult = -1 };
        var vm  = new ComputerDetailViewModel(wmi);
        await vm.LoadAsync(Pc());
        await vm.LoadLiveDetailsAsync();

        Assert.Equal(new[] { "ping:pc1.corp.example.com" }, wmi.Calls);
        Assert.Contains("did not answer ping", vm.WmiError);
        Assert.False(vm.RefreshEventLogCommand.CanExecute(null));
    }

    // ── Client errors become a message ──────────────────────────────────────

    [Fact]
    public async Task ClientError_ShowsMessage_NotEmptyLists()
    {
        var wmi = new FakeWmi { Throw = new UnauthorizedAccessException("Access is denied.") };
        var vm  = new ComputerDetailViewModel(wmi);
        await vm.LoadAsync(Pc());
        await vm.LoadLiveDetailsAsync();

        Assert.Contains("Access denied", vm.WmiError);
        Assert.DoesNotContain("   at ", vm.WmiError);     // no stack trace
        Assert.False(vm.IsLoading);
        // First query failed → the remaining three are not attempted.
        Assert.Equal(new[] { "ping:pc1.corp.example.com", "disks:pc1.corp.example.com" }, wmi.Calls);
    }

    [Theory]
    [InlineData(unchecked((int)0x800706BA), "RPC server unavailable")]
    [InlineData(unchecked((int)0x80070721), "Kerberos authentication failed")]
    [InlineData(unchecked((int)0x80090322), "Kerberos authentication failed")]
    [InlineData(unchecked((int)0x80070005), "Access denied")]
    public void Describe_MapsKnownCodes(int hresult, string expected)
    {
        var msg = WmiErrors.Describe(new COMException("x", hresult));
        Assert.Contains(expected, msg);
        Assert.Contains($"0x{unchecked((uint)hresult):X8}", msg);
    }

    [Fact]
    public void Describe_Unknown_KeepsFirstLineOnly()
    {
        var msg = WmiErrors.Describe(new InvalidOperationException("first line\nsecond line"));
        Assert.StartsWith("WMI query failed: first line", msg);
        Assert.DoesNotContain("second line", msg);
    }

    // ── Kerberos authority builder ──────────────────────────────────────────

    [Theory]
    [InlineData("pc1.corp.example.com", "CORP", @"kerberos:CORP\pc1")]
    [InlineData("SRV-01.ad.local", "AD", @"kerberos:AD\SRV-01")]
    public void Authority_HasKerberosDomainBackslashHost(string fqdn, string domain, string expected)
        => Assert.Equal(expected, WmiAuthority.Build(fqdn, domain));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("PC1")]                   // short name
    [InlineData("pc1.")]
    [InlineData(".corp.example.com")]
    [InlineData("pc1..corp")]
    [InlineData("-pc1.corp")]
    [InlineData("pc1.corp/../x")]
    [InlineData("attacker\\share.corp")]
    [InlineData("pc1.corp.example.com\\root")]
    [InlineData("pc 1.corp")]
    public void Authority_RejectsInvalidHost(string fqdn)
    {
        Assert.False(WmiAuthority.IsValidFqdn(fqdn));
        Assert.Throws<ArgumentException>(() => WmiAuthority.Build(fqdn, "CORP"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("CORP\\X")]
    [InlineData("kerberos:CORP")]
    [InlineData("CO RP")]
    public void Authority_RejectsInvalidDomain(string domain)
        => Assert.Throws<ArgumentException>(() => WmiAuthority.Build("pc1.corp.example.com", domain));

    [Fact]
    public async Task Client_RejectsShortName_WithoutNetwork()
    {
        var client = new AdWmiClient(() => "CORP");
        Assert.Equal(-1L, await client.PingTimeMs("PC1"));
        Assert.Empty(await client.GetDisksAsync("PC1"));
        Assert.Empty(await client.GetLocalAdminsAsync("PC1"));
        Assert.Empty(await client.GetLoggedOnUsersAsync("PC1"));
        Assert.Empty(await client.GetEventLogAsync("PC1", "System", null, 10));
    }

    // ── Source guards: no scope without Kerberos options, no write API ──────

    [Fact]
    public void AdWmiClient_EveryScopeUsesKerberosOptions_AndNoWriteApi()
    {
        var src = File.ReadAllText(Path.Combine(ReadOnlyGuardTests.FindDesktopSourceDir(), "Core", "AD", "AdWmiClient.cs"));
        var scopes = System.Text.RegularExpressions.Regex.Matches(src, @"new ManagementScope\(([^;]*)\);");
        Assert.Single(scopes);
        Assert.Contains("options", scopes[0].Groups[1].Value);
        Assert.Contains("WmiAuthority.Build", src);
        Assert.Contains("ImpersonationLevel.Impersonate", src);
        Assert.Contains("AuthenticationLevel.PacketPrivacy", src);
        Assert.DoesNotContain("catch { }", src);
        Assert.DoesNotContain("InvokeMethod", src);
        Assert.DoesNotContain(".Put(", src);
    }

    [Fact]
    public void ComputerQuery_LoadsDnsHostName()
    {
        var src = File.ReadAllText(Path.Combine(ReadOnlyGuardTests.FindDesktopSourceDir(), "Core", "AD", "AdScanner.cs"));
        var start = src.IndexOf("private List<AdComputer> QueryComputers", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var body = src[start..src.IndexOf("return computers;", start, StringComparison.Ordinal)];
        Assert.Contains("\"dNSHostName\"", body);
        Assert.Contains("DnsHostName", body);
    }
}
