namespace DirHealth.Desktop.Core.AD.Models;

public enum FindingSeverity { Low, Medium, High, Critical }

public class AdFinding
{
    public string Category    { get; set; } = "";
    public string Title       { get; set; } = "";
    public string Description { get; set; } = "";
    public FindingSeverity Severity { get; set; }
    public int Count          { get; set; }
    public List<string> AffectedObjects { get; set; } = [];
    public bool IsAcknowledged { get; set; }
    public string AcknowledgeNote { get; set; } = "";

    public string RemediationUrl => Category switch
    {
        "InactiveUsers" or "StaleDomainAdmins" =>
            "https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/plan/security-best-practices/best-practices-for-securing-active-directory",
        "PasswordNeverExpires" or "ExpiredPasswords" or "PasswordNotRequired" =>
            "https://learn.microsoft.com/en-us/windows/security/threat-protection/security-policy-settings/password-policy",
        "KerberoastableAccounts" or "AsRepRoasting" or "UnconstrainedDelegationComputers" or "UnconstrainedDelegationUsers" =>
            "https://learn.microsoft.com/en-us/windows-server/security/kerberos/kerberos-constrained-delegation-overview",
        "AdminSdHolderAccounts" =>
            "https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/plan/security-best-practices/appendix-c--protected-accounts-and-groups-in-active-directory",
        "EolOperatingSystems" =>
            "https://learn.microsoft.com/en-us/lifecycle/products/",
        "SidHistory" =>
            "https://learn.microsoft.com/en-us/troubleshoot/windows-server/identity/useraccountcontrol-manipulate-account-properties",
        _ => ""
    };
}
