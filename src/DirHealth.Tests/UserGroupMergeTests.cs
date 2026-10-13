using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using Xunit;

namespace DirHealth.Tests;

// S13-2: direct / primary / nested group memberships in the user detail.
public class UserGroupMergeTests
{
    [Theory]
    [InlineData("S-1-5-21-1-2-3-1105", 513, "S-1-5-21-1-2-3-513")]
    [InlineData("S-1-5-21-1-2-3-1105", 512, "S-1-5-21-1-2-3-512")]
    [InlineData("S-1-5-21-4294967295-7-8-500", 515, "S-1-5-21-4294967295-7-8-515")]
    public void PrimaryGroupSid_ReplacesUserRidWithPrimaryGroupId(string userSid, int pgid, string expected)
    {
        Assert.Equal(expected, WellKnownGroups.PrimaryGroupSid(userSid, pgid));
    }

    [Theory]
    [InlineData("S-1-5-32-544")]   // builtin, not a domain account
    [InlineData("S-1-5-21-1-2-3")] // domain SID without RID
    [InlineData("garbage")]
    public void PrimaryGroupSid_RejectsNonAccountSid(string bad)
    {
        Assert.Throws<ArgumentException>(() => WellKnownGroups.PrimaryGroupSid(bad, 513));
    }

    [Fact]
    public void Merge_MarksDirectPrimaryAndNested()
    {
        var result = AdUserGroup.Merge(
            direct:     [("CN=IT-Admins,DC=x", "IT-Admins")],
            primary:    ("CN=Domain Users,DC=x", "Domain Users"),
            transitive: [("CN=IT-Admins,DC=x", "IT-Admins"), ("CN=Domänen-Admins,DC=x", "Domänen-Admins")]);

        Assert.Equal(3, result.Count);
        Assert.Equal(GroupMembershipKind.Direct,  result.Single(g => g.Name == "IT-Admins").Kind);
        Assert.Equal(GroupMembershipKind.Primary, result.Single(g => g.Name == "Domain Users").Kind);
        Assert.Equal(GroupMembershipKind.Nested,  result.Single(g => g.Name == "Domänen-Admins").Kind);
    }

    [Fact]
    public void Merge_DirectWinsOverNested_CaseInsensitiveDn()
    {
        var result = AdUserGroup.Merge(
            direct:     [("CN=G,DC=x", "G")],
            primary:    null,
            transitive: [("cn=g,dc=x", "G")]);
        Assert.Single(result);
        Assert.Equal(GroupMembershipKind.Direct, result[0].Kind);
    }

    [Fact]
    public void Merge_SortsNestedLast()
    {
        var result = AdUserGroup.Merge(
            direct:     [("CN=Z,DC=x", "Z")],
            primary:    ("CN=Domain Users,DC=x", "Domain Users"),
            transitive: [("CN=A,DC=x", "A")]);
        Assert.Equal(["Domain Users", "Z", "A"], result.Select(g => g.Name));
    }

    [Fact]
    public void Merge_EmptyInputs_ReturnsEmpty()
    {
        Assert.Empty(AdUserGroup.Merge([], null, []));
    }

    [Theory]
    [InlineData(GroupMembershipKind.Direct,  "direct")]
    [InlineData(GroupMembershipKind.Primary, "primary")]
    [InlineData(GroupMembershipKind.Nested,  "nested")]
    public void KindLabel_IsReadable(GroupMembershipKind kind, string label)
    {
        Assert.Equal(label, new AdUserGroup { Kind = kind }.KindLabel);
    }
}
