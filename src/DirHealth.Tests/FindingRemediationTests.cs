using DirHealth.Desktop.Core.AD.Models;
using Xunit;

namespace DirHealth.Tests;

public class FindingRemediationTests
{
    // Every category the scanner can emit must return concrete, non-empty remediation guidance (S11-1).
    [Theory]
    [InlineData("InactiveUsers")]
    [InlineData("PasswordNeverExpires")]
    [InlineData("ExpiredPasswords")]
    [InlineData("EmptyGroups")]
    [InlineData("SingleMemberGroups")]
    [InlineData("InactiveComputers")]
    [InlineData("ComputersWithoutOS")]
    [InlineData("KerberoastableAccounts")]
    [InlineData("AdminSdHolderAccounts")]
    [InlineData("WeakPasswordLength")]
    [InlineData("WeakPasswordHistory")]
    [InlineData("NoAccountLockout")]
    [InlineData("EolOperatingSystems")]
    [InlineData("AsRepRoasting")]
    [InlineData("UnconstrainedDelegationComputers")]
    [InlineData("UnconstrainedDelegationUsers")]
    [InlineData("PasswordNotRequired")]
    [InlineData("StaleDomainAdmins")]
    [InlineData("FineGrainedPasswordPolicy")]
    [InlineData("SidHistory")]
    public void Remediation_KnownCategory_IsNonEmpty(string category)
    {
        var f = new AdFinding { Category = category };
        Assert.False(string.IsNullOrWhiteSpace(f.Remediation));
    }

    [Fact]
    public void Remediation_UnknownCategory_IsEmpty()
    {
        var f = new AdFinding { Category = "SomethingNobodyKnows" };
        Assert.Equal("", f.Remediation);
    }

    [Fact]
    public void Remediation_ContainsNoMarkup()
    {
        var f = new AdFinding { Category = "KerberoastableAccounts" };
        Assert.DoesNotContain('<', f.Remediation);
        Assert.DoesNotContain('>', f.Remediation);
    }
}
