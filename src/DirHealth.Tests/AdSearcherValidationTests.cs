using DirHealth.Desktop.Core.AD;
using Xunit;

namespace DirHealth.Tests;

public class AdSearcherValidationTests
{
    // AdConnector with no domain — LDAP calls will throw, but validation gates
    // must return early before reaching any network call for invalid inputs.
    private static AdSearcher MakeSearcher() => new(new AdConnector());

    [Theory]
    [InlineData("S-1-5-EVIL")]
    [InlineData("S-1-5)(objectClass=*)")]
    [InlineData("")]
    [InlineData("admin")]
    [InlineData(" S-1-5-21")]
    [InlineData("S-1-5-21-abc")]
    public async Task SearchBySid_InvalidSid_ReturnsEmptyWithoutHittingLdap(string sid)
    {
        var result = await MakeSearcher().SearchAsync(sid, SearchMode.Sid);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("finance")]
    [InlineData("http://evil.com")]
    [InlineData("")]
    [InlineData("ldap://hostile")]
    [InlineData("free text with spaces")]
    [InlineData("//attacker.example.com")]
    public async Task SearchByOu_InvalidOuPath_ReturnsEmptyWithoutHittingLdap(string ou)
    {
        var result = await MakeSearcher().SearchAsync(ou, SearchMode.Ou);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("OU=Finance,DC=corp,DC=com")]
    [InlineData("CN=Users,DC=corp,DC=com")]
    [InlineData("DC=corp,DC=com")]
    public async Task SearchByOu_ValidOuPath_PassesValidationGateAndReachesLdap(string ou)
    {
        // Valid paths pass the guard and reach LDAP. There is no AD in the test environment,
        // so the bind fails — and since S13-4 that failure surfaces instead of being swallowed
        // into an empty result. An exception here proves the gate let the input through.
        var ex = await Record.ExceptionAsync(() => MakeSearcher().SearchAsync(ou, SearchMode.Ou));
        Assert.NotNull(ex);
        Assert.IsNotType<ArgumentException>(ex);
    }

    // ── S13-4: errors are visible ──────────────────────────────────────────────
    [Fact]
    public async Task SearchByName_LdapFailure_Throws()
    {
        var ex = await Record.ExceptionAsync(() => MakeSearcher().SearchAsync("jdoe", SearchMode.Name));
        Assert.NotNull(ex);
    }

    [Fact]
    public async Task ViewModel_LdapFailure_ShowsErrorNotEmptyState()
    {
        var vm = new DirHealth.Desktop.ViewModels.AdSearchViewModel(MakeSearcher()) { Query = "jdoe" };
        await vm.SearchAsync();

        Assert.True(vm.HasError);
        Assert.False(vm.HasSearched);   // drives the "No results found." empty state
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.StartsWith("Search failed:", vm.StatusMessage);
        Assert.Empty(vm.Results);
    }
}
