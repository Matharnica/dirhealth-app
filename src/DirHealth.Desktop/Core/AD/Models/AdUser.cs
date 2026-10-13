using DirHealth.Desktop.Core.Export;

namespace DirHealth.Desktop.Core.AD.Models;

public class AdUser
{
    public string SamAccountName { get; set; } = "";
    public string DisplayName    { get; set; } = "";
    public string Email          { get; set; } = "";
    /// <summary>lastLogonTimestamp — replicated, may lag the real logon by up to ~14 days.</summary>
    public DateTime? LastLogon   { get; set; }
    public DateTime? PasswordLastSet { get; set; }
    public DateTime? PasswordExpires { get; set; }
    public bool PasswordNeverExpires { get; set; }
    public bool IsEnabled        { get; set; }
    public string DistinguishedName { get; set; } = "";
    public int? DaysUntilPasswordExpiry { get; set; }

    // ── S14-4: attributes for the ADRecon-style export and Copy SID ─────────────
    public string    Sid                { get; set; } = "";
    public int       UserAccountControl { get; set; }
    public DateTime? WhenCreated        { get; set; }
    /// <summary>Null = never expires (accountExpires 0 or 0x7FFFFFFFFFFFFFFF).</summary>
    public DateTime? AccountExpires     { get; set; }
    public string    Department         { get; set; } = "";
    public string    Title              { get; set; } = "";
    /// <summary>Manager DN. Exported only on opt-in (data minimisation).</summary>
    public string    Manager            { get; set; } = "";
    /// <summary>Exported only on opt-in — descriptions often contain passwords.</summary>
    public string    Description        { get; set; } = "";
    public bool      AdminCount         { get; set; }
    public bool      HasSpn             { get; set; }
    /// <summary>servicePrincipalName values (S15-4 detail).</summary>
    public List<string> Spns            { get; set; } = [];
    /// <summary>"Unconstrained", "Constrained (any protocol)", "Constrained" or "".</summary>
    public string    Delegation         { get; set; } = "";

    public string Ou              => DnHelper.OuFromDn(DistinguishedName);
    public bool   NeverLoggedOn   => LastLogon is null;
    public int?   DaysSinceLogon  => AdTime.DaysSince(LastLogon, DateTime.UtcNow);
    public int?   PasswordAgeDays => AdTime.DaysSince(PasswordLastSet, DateTime.UtcNow);
    public bool   PasswordNotRequired => UacFlags.PasswordNotRequired(UserAccountControl);
    public bool   PreAuthNotRequired  => UacFlags.AsRepRoastable(UserAccountControl);
}
