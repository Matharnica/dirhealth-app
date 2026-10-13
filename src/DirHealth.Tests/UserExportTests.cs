using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;
using DirHealth.Desktop.ViewModels;
using DirHealth.Desktop.ViewModels.Tables;
using Xunit;

namespace DirHealth.Tests;

// S14-4: ADRecon-style user export.
public class UserExportTests
{
    private static readonly string[] StandardOrder =
    [
        "SamAccountName", "DisplayName", "Enabled", "OU", "LastLogonApprox (±14 d)", "NeverLoggedOn",
        "DaysSinceLogon", "PwdLastSet", "PasswordAgeDays", "PasswordNeverExpires", "PwdNotRequired",
        "PreAuthNotRequired", "Delegation", "AdminCount", "HasSPN", "WhenCreated", "AccountExpires",
        "Department", "Title",
    ];

    [Fact]
    public void StandardColumns_InSpecifiedOrder()
    {
        var headers = UserColumns.Export(false, false, false).Select(c => c.Header).ToList();
        Assert.Equal(StandardOrder, headers.Take(StandardOrder.Length));
    }

    [Fact]
    public void OptInColumns_AbsentByDefault()
    {
        var headers = UserColumns.Export(false, false, false).Select(c => c.Header).ToList();
        Assert.DoesNotContain("Mail", headers);
        Assert.DoesNotContain("Manager", headers);
        Assert.DoesNotContain("Description", headers);
    }

    [Fact]
    public void OptInColumns_PresentWhenChosen_Individually()
    {
        var onlyDesc = UserColumns.Export(false, false, true).Select(c => c.Header).ToList();
        Assert.Contains("Description", onlyDesc);
        Assert.DoesNotContain("Mail", onlyDesc);
        var all = UserColumns.Export(true, true, true).Select(c => c.Header).ToList();
        Assert.Contains("Mail", all);
        Assert.Contains("Manager", all);
    }

    [Fact]
    public void EveryStoredUacFlag_HasTrueFalseColumn()
    {
        var cols = UserColumns.Export(false, false, false);
        var user = new AdUser { UserAccountControl = UacFlags.DontExpirePassword | UacFlags.Disabled };
        foreach (var (name, bit) in UacFlags.StoredFlags)
        {
            var col = cols.Single(c => c.Header == $"UAC_{name}");
            Assert.Equal(UacFlags.Has(user.UserAccountControl, bit) ? "TRUE" : "FALSE", col.ExportValue(user));
        }
        Assert.Equal("TRUE", cols.Single(c => c.Header == "UAC_DONT_EXPIRE_PASSWORD").ExportValue(user));
        Assert.Equal("FALSE", cols.Single(c => c.Header == "UAC_SMARTCARD_REQUIRED").ExportValue(user));
    }

    [Fact]
    public void MissingDates_AreEmpty_NeverTheWordNever()
    {
        var cols = UserColumns.Export(false, false, false);
        var user = new AdUser { SamAccountName = "svc" };
        foreach (var name in new[] { "LastLogonApprox (±14 d)", "PwdLastSet", "WhenCreated", "AccountExpires", "DaysSinceLogon", "PasswordAgeDays" })
            Assert.Equal("", cols.Single(c => c.Header == name).ExportValue(user));
        Assert.Equal("TRUE", cols.Single(c => c.Header == "NeverLoggedOn").ExportValue(user));
        Assert.DoesNotContain(cols, c => c.ExportValue(user).Contains("Never", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AccountExpires_NeverValues_MapToNull()
    {
        Assert.Null(AdTime.FromAccountExpires(0));
        Assert.Null(AdTime.FromAccountExpires(long.MaxValue));   // FromFileTimeUtc would throw here
        Assert.Equal(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                     AdTime.FromAccountExpires(new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc).ToFileTimeUtc()));
    }

    [Theory]
    [InlineData(UacFlags.TrustedForDelegation, false, "Unconstrained")]
    [InlineData(0, true, "Constrained")]
    [InlineData(UacFlags.TrustedToAuthForDelegation, true, "Constrained (any protocol)")]
    [InlineData(0, false, "")]
    public void DelegationKind_FromUacAndAllowedToDelegateTo(int uac, bool hasTargets, string expected)
    {
        Assert.Equal(expected, AdTime.DelegationKind(uac, hasTargets));
    }

    [Fact]
    public void DaysSince_Floors()
    {
        var now = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(10, AdTime.DaysSince(now.AddDays(-10.7), now));
        Assert.Null(AdTime.DaysSince(null, now));
    }

    [Fact]
    public void ExportFullUsers_WritesOptInOnlyWhenChosen()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dh-users-{Guid.NewGuid():N}.csv");
        var saved = TableViewModel.PickSavePath;
        try
        {
            TableViewModel.PickSavePath = _ => path;
            var vm = new UserBrowserViewModel();
            vm.Table.SetRows([new AdUser { SamAccountName = "a", Description = "pw=Secret1", Email = "a@x" }]);

            vm.ExportFullUsers(new UserExportOptions(FilteredOnly: false));
            var text = File.ReadAllText(path);
            Assert.DoesNotContain("pw=Secret1", text);
            Assert.DoesNotContain("a@x", text);
            Assert.Contains("opt-in columns: none", File.ReadAllText(ExportMetadata.MetaPathFor(path)));

            vm.ExportFullUsers(new UserExportOptions(FilteredOnly: false, IncludeDescription: true));
            Assert.Contains("pw=Secret1", File.ReadAllText(path));
        }
        finally
        {
            TableViewModel.PickSavePath = saved;
            if (File.Exists(path)) File.Delete(path);
            var meta = ExportMetadata.MetaPathFor(path);
            if (File.Exists(meta)) File.Delete(meta);
        }
    }
}
