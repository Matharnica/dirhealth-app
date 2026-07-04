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

    // One concrete, plain-text remediation step per finding category (no markup — reused verbatim in UI and PDF).
    public string Remediation => Category switch
    {
        "InactiveUsers" =>
            "Disable accounts unused for 90+ days, then delete after a retention window. Automate with a scheduled review of lastLogonTimestamp.",
        "PasswordNeverExpires" =>
            "Remove the 'Password never expires' flag on interactive accounts; where a non-expiring secret is unavoidable (service accounts), rotate it on a fixed schedule or migrate to a gMSA.",
        "ExpiredPasswords" =>
            "Force a password change at next logon for these accounts, or disable them if the owner has left.",
        "EmptyGroups" =>
            "Delete groups that serve no purpose. Empty groups clutter delegation reviews and can be silently repopulated by an attacker.",
        "SingleMemberGroups" =>
            "Review whether the group is still needed; consolidate one-off groups to keep the delegation model legible.",
        "InactiveComputers" =>
            "Disable then delete stale computer accounts after confirming the host is decommissioned; stale accounts are a lateral-movement foothold.",
        "ComputersWithoutOS" =>
            "Investigate why the operatingSystem attribute is empty (often non-Windows or never-joined objects) and remove obsolete entries.",
        "KerberoastableAccounts" =>
            "Give service accounts long (25+ char) random passwords or migrate to gMSAs so the extractable TGS hash cannot be cracked offline.",
        "AdminSdHolderAccounts" =>
            "Confirm each adminCount=1 account still needs elevated rights; for removed admins, clear adminCount and re-enable inheritance on the object ACL.",
        "WeakPasswordLength" =>
            "Raise the domain minimum password length to at least 12–14 characters via the Default Domain Policy.",
        "WeakPasswordHistory" =>
            "Increase enforced password history to 10 or more so users cannot cycle back to a recent password.",
        "NoAccountLockout" =>
            "Configure an account-lockout threshold (e.g. 10 attempts / 15 min) to blunt online brute-force attacks.",
        "EolOperatingSystems" =>
            "Upgrade or replace end-of-life systems immediately; isolate any that cannot be upgraded, and prioritise domain controllers.",
        "AsRepRoasting" =>
            "Re-enable Kerberos pre-authentication (clear DONT_REQUIRE_PREAUTH) on these accounts and give them strong passwords.",
        "UnconstrainedDelegationComputers" =>
            "Remove unconstrained delegation from non-DC computers; use resource-based constrained delegation instead, and mark sensitive accounts as 'not delegated'.",
        "UnconstrainedDelegationUsers" =>
            "Remove the TRUSTED_FOR_DELEGATION flag from user accounts; user accounts should almost never be trusted for delegation.",
        "PasswordNotRequired" =>
            "Clear the PASSWD_NOTREQD flag and enforce a strong password on each account so empty passwords are rejected.",
        "StaleDomainAdmins" =>
            "Remove dormant accounts from Domain Admins, or if still required, rotate their passwords and enable strict monitoring.",
        "FineGrainedPasswordPolicy" =>
            "Tighten the Password Settings Object to meet or exceed the domain baseline; never store passwords with reversible encryption.",
        "SidHistory" =>
            "After confirming migrations are complete, clear sIDHistory so migrated accounts no longer carry hidden privileges from the source domain.",
        _ => ""
    };

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
