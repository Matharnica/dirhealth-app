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

    private static readonly Func<bool> NoDns  = () => false;
    private static readonly Func<bool> HasDns = () => true;

    private static IEnumerable<(WellKnownGroupDef, bool, string?)> OptionalMissing(Func<WellKnownGroupDef, bool> missing) =>
        WellKnownGroups.Tier0.Select(d => missing(d) ? (d, false, (string?)GroupNotFoundError) : (d, true, (string?)null));

    [Fact]
    public void Tier0_MissingOptionalGroups_NoDnsRole_AreSkipped_NotFailed()
    {
        var skipped = Tier0SkippedGroups(OptionalMissing(WellKnownGroups.IsOptionalTier0), NoDns);
        Assert.Equal(new[] { "DnsAdmins", "Enterprise Key Admins", "Key Admins" }, skipped.OrderBy(n => n));
    }

    [Fact]
    public void Tier0_KeyAdminsMissing_AreSkipped_RegardlessOfDns()
    {
        var skipped = Tier0SkippedGroups(OptionalMissing(d => d.Rid is 526 or 527 && d.Source != GroupSidSource.Builtin), HasDns);
        Assert.Equal(new[] { "Enterprise Key Admins", "Key Admins" }, skipped.OrderBy(n => n));
    }

    [Fact]
    public void Tier0_DnsAdminsMissing_WhileDnsZonesExist_IsNeverSilentlyPassed()
    {
        // a renamed DnsAdmins must not drop out of the Tier 0 checks (fail-open) — the checks stay Failed
        var ex = Assert.Throws<InvalidOperationException>(() =>
            Tier0SkippedGroups(OptionalMissing(d => d.Source == GroupSidSource.NameOnly), HasDns));
        Assert.Contains("DnsAdmins", ex.Message);
    }

    [Fact]
    public void Tier0_DnsAdminsMissing_DnsStateUnreadable_Throws()
    {
        Assert.ThrowsAny<Exception>(() => Tier0SkippedGroups(
            OptionalMissing(d => d.Source == GroupSidSource.NameOnly),
            () => throw new InvalidOperationException("referral")));
    }

    [Fact]
    public void Tier0_DnsAdminsFound_DnsStateIsNotQueried()
    {
        bool asked = false;
        Tier0SkippedGroups(OptionalMissing(_ => false), () => asked = true);
        Assert.False(asked);
    }

    [Fact]
    public void Tier0_AllPresent_SkipsNothing()
    {
        Assert.Empty(Tier0SkippedGroups(WellKnownGroups.Tier0.Select(d => (d, true, (string?)null)), HasDns));
    }

    [Theory]
    [InlineData(512)]   // Domain Admins
    [InlineData(544)]   // BUILTIN\Administrators
    public void Tier0_MissingMandatoryGroup_Throws(int rid)
    {
        var groups = WellKnownGroups.Tier0.Select(d => (d, d.Rid != rid, d.Rid == rid ? (string?)GroupNotFoundError : null));
        var ex = Assert.Throws<InvalidOperationException>(() => Tier0SkippedGroups(groups, NoDns));
        Assert.Contains("could not be read", ex.Message);
    }

    [Fact]
    public void Tier0_OptionalGroup_WithReadError_StillThrows()
    {
        // "absent" must be proven by a successful search — a failed lookup is a failed check
        var groups = WellKnownGroups.Tier0.Select(d => d.Source == GroupSidSource.NameOnly
            ? (d, false, (string?)"The server is not operational.")
            : (d, true, (string?)null));
        Assert.Throws<InvalidOperationException>(() => Tier0SkippedGroups(groups, NoDns));
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

    // ── M2: never-logged-on accounts only count after the cutoff ─────────────
    private static readonly DateTime Cutoff = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GeneralizedTime_IsLdapFormat()
    {
        Assert.Equal("20260818120000.0Z", CheckFilters.GeneralizedTime(Cutoff));
    }

    public static TheoryData<string> ScannerFilters => new()
    {
        CheckFilters.InactiveUsers(Cutoff),
        CheckFilters.InactiveComputers(Cutoff),
        CheckFilters.StaleDomainAdmins("CN=Domain Admins,CN=Users,DC=c,DC=x", Cutoff),
    };

    [Theory]
    [MemberData(nameof(ScannerFilters))]
    public void ScannerFilter_NeverLoggedOnBranch_RequiresWhenCreatedAtTheSameCutoff(string filter)
    {
        var fileTime = Cutoff.ToFileTimeUtc().ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"(lastLogonTimestamp<={fileTime})", filter);
        Assert.Contains("(&(!(lastLogonTimestamp=*))(whenCreated<=20260818120000.0Z))", filter);
        Assert.DoesNotContain("$", filter);   // no PowerShell leaks into the scanner's filter
    }

    [Theory]
    [InlineData("InactiveUsers",     CheckFilters.InactiveUserDays)]
    [InlineData("InactiveComputers", CheckFilters.InactiveComputerDays)]
    [InlineData("StaleDomainAdmins", CheckFilters.StaleAdminDays)]
    public void FixBlock_NeverLoggedOnBranch_ComputesWhenCreatedFromTheSameThreshold(string category, int days)
    {
        var rule = FindingCatalog.Get(category)!;
        Assert.Contains($"(whenCreated<={CheckFilters.PsGeneralizedCutoff(days)})", rule.CheckFilter);
        Assert.Contains($"-LDAPFilter \"{rule.CheckFilter}\"", rule.Fix);   // fix block = scan filter
        Assert.Contains($"AddDays(-{days})", rule.Fix);                     // $cutoff uses the same days
    }
}
