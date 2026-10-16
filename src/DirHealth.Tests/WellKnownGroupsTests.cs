using DirHealth.Desktop.Core.AD;
using Xunit;

namespace DirHealth.Tests;

// S13-1: admin groups are found by SID, not by (localized) name.
public class WellKnownGroupsTests
{
    private const string DomainSid = "S-1-5-21-1004336348-1177238915-682003330";

    [Theory]
    [InlineData(512, "S-1-5-21-1004336348-1177238915-682003330-512")]
    [InlineData(518, "S-1-5-21-1004336348-1177238915-682003330-518")]
    [InlineData(519, "S-1-5-21-1004336348-1177238915-682003330-519")]
    [InlineData(526, "S-1-5-21-1004336348-1177238915-682003330-526")]
    [InlineData(527, "S-1-5-21-1004336348-1177238915-682003330-527")]
    public void DomainGroupSid_AppendsRid(int rid, string expected)
    {
        Assert.Equal(expected, WellKnownGroups.DomainGroupSid(DomainSid, rid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("S-1-5-32")]
    [InlineData("S-1-5-21-1-2")]                      // too few sub-authorities
    [InlineData("S-1-5-21-1-2-3-512")]                // already an account SID
    [InlineData("S-1-5-21-1-2-3)(objectClass=*")]     // filter break-out attempt
    public void DomainGroupSid_RejectsMalformedDomainSid(string bad)
    {
        Assert.Throws<ArgumentException>(() => WellKnownGroups.DomainGroupSid(bad, 512));
    }

    [Theory]
    [InlineData(WellKnownGroups.BuiltinAdministratorsRid, "S-1-5-32-544")]
    [InlineData(WellKnownGroups.AccountOperatorsRid,      "S-1-5-32-548")]
    [InlineData(WellKnownGroups.ServerOperatorsRid,       "S-1-5-32-549")]
    [InlineData(WellKnownGroups.PrintOperatorsRid,        "S-1-5-32-550")]
    [InlineData(WellKnownGroups.BackupOperatorsRid,       "S-1-5-32-551")]
    [InlineData(WellKnownGroups.RemoteDesktopUsersRid,    "S-1-5-32-555")]
    public void BuiltinGroupSid_MatchesWellKnownSid(int rid, string expected)
    {
        Assert.Equal(expected, WellKnownGroups.BuiltinGroupSid(rid));
    }

    [Fact]
    public void SidFor_UsesForestRootSidForEnterpriseAdmins()
    {
        const string forest = "S-1-5-21-9-8-7";
        var ea = WellKnownGroups.Privileged.Single(d => d.Rid == WellKnownGroups.EnterpriseAdminsRid);
        Assert.Equal("S-1-5-21-9-8-7-519", WellKnownGroups.SidFor(ea, DomainSid, forest));
    }

    [Fact]
    public void SidFor_ReturnsNullWhenSidUnknownOrNameOnly()
    {
        Assert.Null(WellKnownGroups.SidFor(WellKnownGroups.DomainAdmins, null, null));
        var dns = WellKnownGroups.Privileged.Single(d => d.Source == GroupSidSource.NameOnly);
        Assert.Equal("DnsAdmins", dns.FallbackName);
        Assert.Null(WellKnownGroups.SidFor(dns, DomainSid, DomainSid));
    }

    [Fact]
    public void Privileged_ContainsEveryAcceptanceCriterionGroup()
    {
        var rids = WellKnownGroups.Privileged.Select(d => d.Rid).ToHashSet();
        foreach (var rid in new[] { 512, 518, 519, 526, 527, 544, 548, 549, 550, 551, 555 })
            Assert.Contains(rid, rids);
    }

    [Fact]
    public void SidBytesToString_DecodesBuiltinAdministrators()
    {
        // S-1-5-32-544: rev 1, 2 sub-authorities, authority 5, 32, 544
        byte[] bytes = [1, 2, 0, 0, 0, 0, 0, 5, 32, 0, 0, 0, 0x20, 0x02, 0, 0];
        Assert.Equal("S-1-5-32-544", WellKnownGroups.SidBytesToString(bytes));
    }

    [Fact]
    public void SidBytesToString_DecodesDomainSidWithLargeSubAuthorities()
    {
        // S-1-5-21-4294967295-1-2 — sub-authorities are unsigned 32-bit little-endian
        byte[] bytes =
        [
            1, 4, 0, 0, 0, 0, 0, 5,
            21, 0, 0, 0,
            0xFF, 0xFF, 0xFF, 0xFF,
            1, 0, 0, 0,
            2, 0, 0, 0,
        ];
        Assert.Equal("S-1-5-21-4294967295-1-2", WellKnownGroups.SidBytesToString(bytes));
    }

    [Fact]
    public void SidBytesToString_RejectsTruncatedInput()
    {
        byte[] bytes = [1, 2, 0, 0, 0, 0, 0, 5, 32, 0, 0, 0];
        Assert.Throws<ArgumentException>(() => WellKnownGroups.SidBytesToString(bytes));
    }

    [Fact]
    public void MembershipClause_IncludesPrimaryGroupWhenGiven()
    {
        var clause = WellKnownGroups.MembershipClause("CN=Dom\\c3\\a4nen-Admins,DC=x", 512);
        Assert.Equal("(|(memberOf:1.2.840.113556.1.4.1941:=CN=Dom\\c3\\a4nen-Admins,DC=x)(primaryGroupID=512))", clause);
        Assert.DoesNotContain("primaryGroupID", WellKnownGroups.MembershipClause("CN=G,DC=x", null));
    }

    [Theory]
    [InlineData("DC=corp,DC=example,DC=com", "corp.example.com")]
    [InlineData("dc=corp, dc=local",         "corp.local")]
    public void DnToDnsName_JoinsDcComponents(string dn, string expected)
    {
        Assert.Equal(expected, AdConnector.DnToDnsName(dn));
    }
}
