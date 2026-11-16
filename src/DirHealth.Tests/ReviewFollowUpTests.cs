using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.ViewModels;
using DirHealth.Desktop.ViewModels.Tables;
using Xunit;
using static DirHealth.Desktop.Core.AD.Checks.CheckLogic;

namespace DirHealth.Tests;

/// <summary>Follow-ups to the core review 2026-11-16 (PR #47): DnsAdmins warning in the fix scripts, unreadable password expiry in the user browser.</summary>
public class ReviewFollowUpTests
{
    // ── Tier 0 preamble: DnsAdmins by name must not fail silently ─────────────
    [Fact]
    public void Tier0Scripts_WarnWhenDnsAdminsIsMissing()
    {
        var scripts = FindingCatalog.All.Where(r => r.Fix.Contains("DnsAdmins")).ToList();
        Assert.NotEmpty(scripts);
        foreach (var r in scripts)
        {
            Assert.Contains("Write-Warning", r.Fix);
            Assert.DoesNotContain("$t0 += Get-ADGroup -Filter", r.Fix);   // no silent append of a possibly empty result
        }
    }

    // ── User browser: "no expiry" and "not readable" stay distinguishable ─────
    [Theory]
    [InlineData(PasswordExpiryState.Expires,    "Expires")]
    [InlineData(PasswordExpiryState.NoExpiry,   AdUser.NoExpiryText)]
    [InlineData(PasswordExpiryState.Unreadable, AdUser.UnreadableExpiryText)]
    public void ExpiryStatus_NamesEachState(PasswordExpiryState state, string expected) =>
        Assert.Equal(expected, new AdUser { PasswordExpiryState = state }.PasswordExpiryStatus);

    [Fact]
    public void ExpiryStatus_EmptyWhenNotLoaded() =>
        Assert.Equal("", new AdUser().PasswordExpiryStatus);

    [Fact]
    public void Grid_ShowsExpiryStatusColumn()
    {
        var col = Assert.Single(UserColumns.Grid, c => c.BindingPath == nameof(AdUser.PasswordExpiryStatus));
        Assert.Equal(AdUser.UnreadableExpiryText, col.ExportValue(new AdUser { PasswordExpiryState = PasswordExpiryState.Unreadable }));
        Assert.Equal(AdUser.NoExpiryText,         col.ExportValue(new AdUser { PasswordExpiryState = PasswordExpiryState.NoExpiry }));
    }

    [Fact]
    public void ExpiryNotice_NullWhenAllRead()
    {
        var users = new List<AdUser>
        {
            new() { PasswordExpiryState = PasswordExpiryState.Expires },
            new() { PasswordExpiryState = PasswordExpiryState.NoExpiry },
        };
        Assert.Null(UserBrowserViewModel.ExpiryNotice(users));
    }

    [Fact]
    public void ExpiryNotice_CountsUnreadableUsers()
    {
        var users = new List<AdUser>
        {
            new() { PasswordExpiryState = PasswordExpiryState.Unreadable },
            new() { PasswordExpiryState = PasswordExpiryState.Unreadable },
            new() { PasswordExpiryState = PasswordExpiryState.NoExpiry },
        };
        var notice = UserBrowserViewModel.ExpiryNotice(users);
        Assert.NotNull(notice);
        Assert.Contains("2 of 3", notice);
        Assert.Contains(AdUser.UnreadableExpiryText, notice);
    }
}
