using System.Globalization;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

/// <summary>One name/value line of the user detail and the dossier (S15-4/S15-5).</summary>
public record DetailRow(string Label, string Value, bool Warn = false);

/// <summary>
/// Builds the user detail's name/value list. Both logon fields are labelled for what they
/// are: lastLogonTimestamp is replicated but up to ~14 days behind; lastLogon is exact but
/// only for the one DC that answered. Pure, testable.
/// </summary>
public static class UserDetailRows
{
    private static string D(DateTime? d) => d?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—";
    private static string YesNo(bool b) => b ? "Yes" : "No";

    public static List<DetailRow> Build(AdUser u, DateTime? lastLogonThisDc)
    {
        var flags = UacFlags.StoredFlags.Where(f => UacFlags.Has(u.UserAccountControl, f.Bit)).Select(f => f.Name).ToList();
        bool kerberoastable = u.HasSpn && u.IsEnabled;

        return
        [
            new("Display Name",                    u.DisplayName),
            new("sAMAccountName",                  u.SamAccountName),
            new("Email",                           string.IsNullOrEmpty(u.Email) ? "—" : u.Email),
            new("Enabled",                         YesNo(u.IsEnabled), Warn: !u.IsEnabled),
            new("OU",                              string.IsNullOrEmpty(u.Ou) ? "—" : u.Ou),
            new("SID",                             string.IsNullOrEmpty(u.Sid) ? "—" : u.Sid),
            new("When Created",                    D(u.WhenCreated)),
            new("Account Expires",                 u.AccountExpires is null ? "Never" : D(u.AccountExpires)),
            new("Last Logon (replicated, ±14 d)",  u.LastLogon is null ? "No logon recorded" : D(u.LastLogon)),
            new("Last Logon (this DC)",            lastLogonThisDc is null ? "No logon on this DC" : D(lastLogonThisDc)),
            new("Password Last Set",               D(u.PasswordLastSet)),
            new("Password Age",                    u.PasswordAgeDays is { } a ? $"{a} days" : "—"),
            new("Password Never Expires",          YesNo(u.PasswordNeverExpires), Warn: u.PasswordNeverExpires),
            new("Password Not Required",           YesNo(u.PasswordNotRequired),  Warn: u.PasswordNotRequired),
            new("Pre-Auth Not Required (AS-REP)",  YesNo(u.PreAuthNotRequired),   Warn: u.PreAuthNotRequired),
            new("Delegation",                      string.IsNullOrEmpty(u.Delegation) ? "None" : u.Delegation,
                                                   Warn: u.Delegation == "Unconstrained"),
            new("SPNs",                            u.Spns.Count == 0 ? "None" : string.Join(", ", u.Spns)),
            new("Kerberoastable",                  YesNo(kerberoastable), Warn: kerberoastable),
            new("adminCount",                      u.AdminCount ? "1 (protected by AdminSDHolder)" : "0"),
            new("Department",                      string.IsNullOrEmpty(u.Department) ? "—" : u.Department),
            new("Title",                           string.IsNullOrEmpty(u.Title) ? "—" : u.Title),
            new("Manager",                         string.IsNullOrEmpty(u.Manager) ? "—" : u.Manager),
            new("Description",                     string.IsNullOrEmpty(u.Description) ? "—" : u.Description),
            new("UAC Flags",                       flags.Count == 0 ? "—" : string.Join(", ", flags)),
            new("Distinguished Name",              u.DistinguishedName),
        ];
    }

    /// <summary>Findings whose affected objects name this user (by sAMAccountName or DN).</summary>
    public static List<AdFinding> FindingsFor(AdUser u, IEnumerable<AdFinding> findings) =>
        findings.Where(f => f.AffectedObjects.Any(o =>
                    string.Equals(o, u.SamAccountName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(o, u.DistinguishedName, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(f => f.Severity)
                .ToList();
}
