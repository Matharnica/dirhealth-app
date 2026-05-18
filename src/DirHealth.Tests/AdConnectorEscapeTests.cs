using DirHealth.Desktop.Core.AD;
using Xunit;

namespace DirHealth.Tests;

public class AdConnectorEscapeTests
{
    [Theory]
    [InlineData("jdoe",          "jdoe")]
    [InlineData("",              "")]
    [InlineData("corp\\evil",    "corp\\5cevil")]
    [InlineData("*",             "\\2a")]
    [InlineData("(",             "\\28")]
    [InlineData(")",             "\\29")]
    [InlineData("a\0b",          "a\\00b")]
    [InlineData("\\*()\0",       "\\5c\\2a\\28\\29\\00")]
    public void EscapeFilterValue_EscapesRfc4515Characters(string input, string expected)
    {
        Assert.Equal(expected, AdConnector.EscapeFilterValue(input));
    }

    [Fact]
    public void EscapeFilterValue_WildcardInjection_IsNeutralized()
    {
        // (sAMAccountName=*) would dump all users — ensure * is escaped
        var escaped = AdConnector.EscapeFilterValue("*");
        Assert.DoesNotContain("*", escaped);
    }

    [Fact]
    public void EscapeFilterValue_FilterBreakout_IsNeutralized()
    {
        // admin)(|(cn=*) is a classic filter break-out payload
        var escaped = AdConnector.EscapeFilterValue("admin)(|(cn=*)");
        Assert.DoesNotContain(")(", escaped);
        Assert.DoesNotContain("*", escaped);
    }
}
