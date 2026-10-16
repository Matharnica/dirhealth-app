using DirHealth.Desktop.Core.AD;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export;

namespace DirHealth.Desktop.ViewModels.Tables;

/// <summary>Column sets for user tables (S14-1 grid, S14-4 ADRecon-style export).</summary>
public static class UserColumns
{
    /// <summary>"Last Logon" is lastLogonTimestamp — replicated and up to ~14 days behind (A10).</summary>
    public const string LastLogonHeader = "Last Logon (replicated, ±14 d)";

    /// <summary>What the user browser grid shows (and what its quick CSV exports).</summary>
    public static IReadOnlyList<TableColumn<AdUser>> Grid { get; } =
    [
        new("Display Name",       u => u.DisplayName,         nameof(AdUser.DisplayName)),
        new("sAMAccountName",     u => u.SamAccountName,      nameof(AdUser.SamAccountName), monospace: true),
        new("Email",              u => u.Email,               nameof(AdUser.Email)),
        new("Enabled",            u => u.IsEnabled,           nameof(AdUser.IsEnabled), yesNo: true),
        new(LastLogonHeader,      u => u.LastLogon,           nameof(AdUser.LastLogon), "yyyy-MM-dd", monospace: true),
        new("Password Last Set",  u => u.PasswordLastSet,     nameof(AdUser.PasswordLastSet), "yyyy-MM-dd", monospace: true),
        new("Password Expires In (d)", u => u.DaysUntilPasswordExpiry, nameof(AdUser.DaysUntilPasswordExpiry)),
        new("OU",                 u => u.Ou,                  nameof(AdUser.Ou)),
    ];

    /// <summary>
    /// ADRecon-style full user export (S14-4). Mail, Manager and Description only on opt-in:
    /// data minimisation, and descriptions often contain passwords. Dates ISO, empty when
    /// unknown — never "Never"; the "NeverLoggedOn" bool carries that fact instead.
    /// </summary>
    public static IReadOnlyList<TableColumn<AdUser>> Export(bool includeMail, bool includeManager, bool includeDescription)
    {
        var cols = new List<TableColumn<AdUser>>
        {
            new("SamAccountName",           u => u.SamAccountName),
            new("DisplayName",              u => u.DisplayName),
            new("Enabled",                  u => u.IsEnabled),
            new("OU",                       u => u.Ou),
            new("LastLogonApprox (±14 d)",  u => u.LastLogon),
            new("NeverLoggedOn",            u => u.NeverLoggedOn),
            new("DaysSinceLogon",           u => u.DaysSinceLogon),
            new("PwdLastSet",               u => u.PasswordLastSet),
            new("PasswordAgeDays",          u => u.PasswordAgeDays),
            new("PasswordNeverExpires",     u => u.PasswordNeverExpires),
            new("PwdNotRequired",           u => u.PasswordNotRequired),
            new("PreAuthNotRequired",       u => u.PreAuthNotRequired),
            new("Delegation",               u => u.Delegation),
            new("AdminCount",               u => u.AdminCount),
            new("HasSPN",                   u => u.HasSpn),
            new("WhenCreated",              u => u.WhenCreated),
            new("AccountExpires",           u => u.AccountExpires),
            new("Department",               u => u.Department),
            new("Title",                    u => u.Title),
        };
        if (includeMail)        cols.Add(new("Mail",        u => u.Email));
        if (includeManager)     cols.Add(new("Manager",     u => u.Manager));
        if (includeDescription) cols.Add(new("Description", u => u.Description));

        cols.Add(new("SID",               u => u.Sid));
        cols.Add(new("DistinguishedName", u => u.DistinguishedName));
        foreach (var (name, bit) in UacFlags.StoredFlags)
            cols.Add(new($"UAC_{name}", u => UacFlags.Has(u.UserAccountControl, bit)));
        return cols;
    }
}
