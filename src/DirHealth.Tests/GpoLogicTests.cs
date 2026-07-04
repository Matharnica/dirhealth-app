using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using Xunit;

namespace DirHealth.Tests;

public class GpoLogicTests
{
    private const string GuidA = "{31B2F340-016D-11D2-945F-00C04FB984F9}";
    private const string GuidB = "{6AC1786C-016F-11D2-945F-00C04FB984F9}";

    [Fact]
    public void ExtractGuids_SingleLink_ReturnsGuid()
    {
        var link = $"[LDAP://cn={GuidA},cn=policies,cn=system,DC=corp,DC=com;0]";
        Assert.Equal(new[] { GuidA }, GpoLogic.ExtractGuids(link).ToArray());
    }

    [Fact]
    public void ExtractGuids_MultipleLinks_ReturnsAllInOrder()
    {
        var link = $"[LDAP://cn={GuidA},cn=policies,cn=system,DC=corp,DC=com;0]" +
                   $"[LDAP://cn={GuidB},cn=policies,cn=system,DC=corp,DC=com;2]";
        Assert.Equal(new[] { GuidA, GuidB }, GpoLogic.ExtractGuids(link).ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no guids here")]
    public void ExtractGuids_NoGuids_ReturnsEmpty(string? link)
    {
        Assert.Empty(GpoLogic.ExtractGuids(link));
    }

    [Theory]
    [InlineData(0, 0, "Orphaned")]  // linked nowhere
    [InlineData(0, 2, "Active")]    // linked and enabled
    [InlineData(3, 5, "Disabled")]  // fully disabled wins even when linked
    [InlineData(3, 0, "Disabled")]  // disabled wins over orphaned
    [InlineData(1, 0, "Orphaned")]  // only user-half disabled → not fully disabled
    public void Classify_ReturnsExpectedStatus(int flags, int linkCount, string expected)
    {
        Assert.Equal(expected, GpoLogic.Classify(flags, linkCount));
    }

    [Fact]
    public void AttributeCompleteness_Percent_RoundsToOneDecimal()
    {
        var a = new AdAttributeCompleteness { FilledCount = 1, TotalCount = 3 };
        Assert.Equal(33.3, a.Percent);
        Assert.Equal("33.3%", a.PercentLabel);
        Assert.Equal("1 / 3", a.CountLabel);
    }

    [Fact]
    public void AttributeCompleteness_ZeroTotal_IsZeroPercent_NoDivideByZero()
    {
        var a = new AdAttributeCompleteness { FilledCount = 0, TotalCount = 0 };
        Assert.Equal(0, a.Percent);
    }

    [Fact]
    public void AttributeCompleteness_AllFilled_Is100Percent()
    {
        var a = new AdAttributeCompleteness { FilledCount = 50, TotalCount = 50 };
        Assert.Equal(100, a.Percent);
    }
}
