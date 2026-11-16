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
}
