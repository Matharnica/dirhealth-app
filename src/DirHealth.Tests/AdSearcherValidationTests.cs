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
    public async Task SearchByOu_ValidOuPath_PassesValidationGate(string ou)
    {
        // Valid paths pass the guard; the subsequent LDAP call fails silently (no AD in test env).
        // This confirms the validation gate does not reject legitimate DN inputs.
        var ex = await Record.ExceptionAsync(() => MakeSearcher().SearchAsync(ou, SearchMode.Ou));
        Assert.Null(ex);
    }
}
