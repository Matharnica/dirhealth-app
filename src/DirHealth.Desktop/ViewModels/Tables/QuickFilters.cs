using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.ViewModels.Tables;

/// <summary>
/// Local quick-filter chips (S15-6). They narrow the already loaded rows — no new LDAP query —
/// and combine with AND. Kept separate from the server search on purpose (ADAC pitfall: users
/// confuse "filter what is shown" with "search the directory"). Pure, testable.
/// </summary>
public static class QuickFilters
{
    public const int InactiveDays = 90;

    public record UserChips(bool Disabled = false, bool PasswordNeverExpires = false, bool Admin = false, bool Inactive = false)
    {
        public bool Any => Disabled || PasswordNeverExpires || Admin || Inactive;
    }

    public record ComputerChips(bool Disabled = false, bool Inactive = false)
    {
        public bool Any => Disabled || Inactive;
    }

    /// <summary>"Inactive" = no replicated logon within 90 days, or never logged on.</summary>
    public static bool IsInactive(DateTime? lastLogonUtc, DateTime nowUtc) =>
        lastLogonUtc is null || (nowUtc - lastLogonUtc.Value).TotalDays > InactiveDays;

    /// <summary>"Admin" = adminCount=1, i.e. protected by AdminSDHolder (current or former admin).</summary>
    public static bool Matches(AdUser u, UserChips c, DateTime nowUtc) =>
        (!c.Disabled             || !u.IsEnabled) &&
        (!c.PasswordNeverExpires || u.PasswordNeverExpires) &&
        (!c.Admin                || u.AdminCount) &&
        (!c.Inactive             || IsInactive(u.LastLogon, nowUtc));

    public static bool Matches(AdComputer comp, ComputerChips c, DateTime nowUtc) =>
        (!c.Disabled || !comp.IsEnabled) &&
        (!c.Inactive || IsInactive(comp.LastLogon, nowUtc));
}
