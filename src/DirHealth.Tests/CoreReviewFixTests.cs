using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Checks;
using DirHealth.Desktop.Core.AD.Models;
using Xunit;
using static DirHealth.Desktop.Core.AD.Checks.CheckLogic;

namespace DirHealth.Tests;

// Fixes from the core review 2026-11-16 (docs/research/2026-11-16-review/review-core.md): H2, H3, M1, M2.
public class CoreReviewFixTests
{
    private static readonly DateTime Now = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    // ── H2: password expiry from msDS-UserPasswordExpiryTimeComputed ────────
    [Fact]
    public void Expiry_ComputedValue_GivesDateAndDays()
    {
        var at = Now.AddDays(42);   // e.g. default domain policy 42 days, password set today
        var e  = PasswordExpiryFromComputed(at.ToFileTimeUtc());
        Assert.Equal(PasswordExpiryState.Expires, e.State);
        Assert.Equal(at, e.ExpiresUtc);
        Assert.Equal(42, DaysUntilExpiry(e, Now));
    }

    [Fact]
    public void Expiry_PsoValue_IsTakenAsIs_NoFixedDefault()
    {
        // A PSO with maxPwdAge 10 days: AD already computed pwdLastSet + 10 d — no 90-day assumption anywhere
        var pwdLastSet = Now.AddDays(-50);
        var e = PasswordExpiryFromComputed(pwdLastSet.AddDays(10).ToFileTimeUtc());
        Assert.Equal(-40, DaysUntilExpiry(e, Now));                 // expired 40 days ago
        Assert.NotEqual(40, DaysUntilExpiry(e, Now));               // the old code showed "+40" (90 − 50)
    }

    [Theory]
    [InlineData(long.MaxValue)]   // 0x7FFFFFFFFFFFFFFF: DONT_EXPIRE_PASSWORD, smartcard, policy "never expires"
    [InlineData(0L)]              // pwdLastSet = 0: must change at next logon, no expiry date
    public void Expiry_NeverOrNotSet_HasNoDate(long raw)
    {
        var e = PasswordExpiryFromComputed(raw);
        Assert.Equal(PasswordExpiryState.NoExpiry, e.State);
        Assert.Null(e.ExpiresUtc);
        Assert.Null(DaysUntilExpiry(e, Now));
    }

    [Fact]
    public void Expiry_DomainPolicyNeverExpires_IsNoExpiry_EvenWithoutUacFlag()
    {
        // maxPwdAge = 0x8000000000000000 ("never") → AD returns 0x7FFF… for every user, flag or not
        Assert.Equal(PasswordExpiryState.NoExpiry, PasswordExpiryFromComputed(0x7FFFFFFFFFFFFFFF).State);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1L)]
    public void Expiry_Unreadable_IsNeverGuessed(long? raw)
    {
        var e = PasswordExpiryFromComputed(raw);
        Assert.Equal(PasswordExpiryState.Unreadable, e.State);
        Assert.Null(e.ExpiresUtc);
        Assert.Null(DaysUntilExpiry(e, Now));
    }

    [Fact]
    public void ExpiringWithin_KeepsWindow_SoonestFirst()
    {
        var users = new List<AdUser>
        {
            new() { SamAccountName = "late",    DaysUntilPasswordExpiry = 25 },
            new() { SamAccountName = "expired", DaysUntilPasswordExpiry = -3 },
            new() { SamAccountName = "never",   DaysUntilPasswordExpiry = null },
            new() { SamAccountName = "soon",    DaysUntilPasswordExpiry = 2 },
            new() { SamAccountName = "far",     DaysUntilPasswordExpiry = 31 },
        };
        Assert.Equal(new[] { "soon", "late" }, ExpiringWithin(users, 30).Select(u => u.SamAccountName));
    }

    // ── H3: optional vs mandatory Tier 0 groups ──────────────────────────────
    private static WellKnownGroupDef Tier0(Func<WellKnownGroupDef, bool> pick) => WellKnownGroups.Tier0.Single(pick);

    [Fact]
    public void Tier0_OptionalGroups_AreExactlyDnsAdminsKeyAdminsEnterpriseKeyAdmins()
    {
        var optional = WellKnownGroups.Tier0.Where(WellKnownGroups.IsOptionalTier0).Select(d => d.FallbackName).OrderBy(n => n);
        Assert.Equal(new[] { "DnsAdmins", "Enterprise Key Admins", "Key Admins" }, optional);
        // the core groups stay mandatory
        foreach (var rid in new[] { 512, 517, 518, 519 })
            Assert.False(WellKnownGroups.IsOptionalTier0(Tier0(d => d.Rid == rid && d.Source != GroupSidSource.Builtin)));
        Assert.False(WellKnownGroups.IsOptionalTier0(Tier0(d => d.Source == GroupSidSource.Builtin && d.Rid == 544)));
    }

    [Fact]
    public void Tier0_MissingOptionalGroups_AreSkipped_NotFailed()
    {
        var groups = WellKnownGroups.Tier0.Select(d => WellKnownGroups.IsOptionalTier0(d)
            ? (d, false, (string?)GroupNotFoundError)
            : (d, true, (string?)null));
        Assert.Equal(new[] { "DnsAdmins", "Enterprise Key Admins", "Key Admins" }, Tier0SkippedGroups(groups).OrderBy(n => n));
    }

    [Fact]
    public void Tier0_AllPresent_SkipsNothing()
    {
        Assert.Empty(Tier0SkippedGroups(WellKnownGroups.Tier0.Select(d => (d, true, (string?)null))));
    }

    [Theory]
    [InlineData(512)]   // Domain Admins
    [InlineData(544)]   // BUILTIN\Administrators
    public void Tier0_MissingMandatoryGroup_Throws(int rid)
    {
        var groups = WellKnownGroups.Tier0.Select(d => (d, d.Rid != rid, d.Rid == rid ? (string?)GroupNotFoundError : null));
        var ex = Assert.Throws<InvalidOperationException>(() => Tier0SkippedGroups(groups));
        Assert.Contains("could not be read", ex.Message);
    }

    [Fact]
    public void Tier0_OptionalGroup_WithReadError_StillThrows()
    {
        // "absent" must be proven by a successful search — a failed lookup is a failed check
        var groups = WellKnownGroups.Tier0.Select(d => d.Source == GroupSidSource.NameOnly
            ? (d, false, (string?)"The server is not operational.")
            : (d, true, (string?)null));
        Assert.Throws<InvalidOperationException>(() => Tier0SkippedGroups(groups));
    }

    [Fact]
    public void Tier0_GroupNotFoundError_MatchesAdConnector()
    {
        // drift guard: "absent" is recognised by AdConnector.FindGroup's error text
        var src = File.ReadAllText(Path.Combine(ReadOnlyGuardTests.FindDesktopSourceDir(), "Core", "AD", "AdConnector.cs"));
        Assert.Contains($"\"{GroupNotFoundError}\"", src);
    }

    // ── M1: primary-group groups are not "empty" ─────────────────────────────
    [Theory]
    [InlineData("S-1-5-21-100-200-300-513", true)]    // Domain Users
    [InlineData("S-1-5-21-100-200-300-514", true)]    // Domain Guests
    [InlineData("S-1-5-21-100-200-300-515", true)]    // Domain Computers
    [InlineData("S-1-5-21-100-200-300-516", true)]    // Domain Controllers
    [InlineData("S-1-5-21-100-200-300-521", true)]    // Read-only Domain Controllers
    [InlineData("S-1-5-21-100-200-300-512", false)]   // Domain Admins — members in member
    [InlineData("S-1-5-21-100-200-300-1513", false)]  // ordinary group whose RID merely ends in 513
    [InlineData("S-1-5-32-513", false)]               // not a domain SID
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EmptyGroups_PrimaryGroupsAreRecognisedByRid(string? sid, bool primary) =>
        Assert.Equal(primary, IsPrimaryGroupSid(sid));

    [Fact]
    public void EmptyGroups_Filter_ExcludesCriticalSystemGroups_AndFixBlockUsesTheSameFilter()
    {
        Assert.Contains("(!(isCriticalSystemObject=TRUE))", CheckFilters.EmptyGroups);
        Assert.Contains($"-LDAPFilter \"{CheckFilters.EmptyGroups}\"", FindingCatalog.Get("EmptyGroups")!.Fix);
    }
}
