using System.Text.RegularExpressions;

namespace DirHealth.Desktop.Core.AD;

public record FindingReference(string Label, string Url);

/// <summary>
/// Everything DirHealth knows about one rule (S16-1/S16-3): stable id, weight, the three
/// texts of the finding card, references, and the fix block. The fix block is shown and
/// copied — DirHealth never runs it (read-only invariant; ReadOnlyGuardTests stays green).
/// </summary>
/// <param name="Weight">1–10; exploitable findings weigh more than hygiene (input for S17).</param>
/// <param name="CheckFilter">The check's LDAP filter as it appears in <see cref="Fix"/>; null for
/// domain-setting checks that read attributes instead of searching.</param>
public record FindingRule(
    string Category, string RuleId, int Weight,
    string What, string Why, string HowToFix,
    IReadOnlyList<FindingReference> References,
    string Fix, string FixRisk, string Revert,
    string? CheckFilter)
{
    /// <summary>How the rule feeds the score (ADR 0002). Set from <see cref="FindingCatalog"/>'s scoring table.</summary>
    public ScoreMeasure Measure    { get; init; } = ScoreMeasure.Count;
    /// <summary>Count: objects at which the rule is fully present. Share: fraction of the population.</summary>
    public double       Saturation { get; init; } = 1;
    public ScorePopulation Population { get; init; } = ScorePopulation.None;
    /// <summary>Exception key type (ADR 0001): object rules by SID, policy rules by name.</summary>
    public bool KeyedBySid => Measure != ScoreMeasure.Presence && !FindingCatalog.NameKeyed.Contains(Category);
}

/// <summary>Score measure of a rule (ADR 0002).</summary>
public enum ScoreMeasure { Count, Share, Presence, Informational }

/// <summary>Population a Share rule is measured against.</summary>
public enum ScorePopulation { None, Users, Groups, Computers }

/// <summary>Rule catalog — single source for ids, weights, texts and fix blocks.</summary>
public static class FindingCatalog
{
    private const string Ps = "Import-Module ActiveDirectory";

    // ── References (only links whose target is certain) ───────────────────────
    private static readonly FindingReference MsSecuringAd = new("Microsoft: Best practices for securing AD",
        "https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/plan/security-best-practices/best-practices-for-securing-active-directory");
    private static readonly FindingReference MsPasswordPolicy = new("Microsoft: Password policy",
        "https://learn.microsoft.com/en-us/windows/security/threat-protection/security-policy-settings/password-policy");
    private static readonly FindingReference MsLockout = new("Microsoft: Account lockout policy",
        "https://learn.microsoft.com/en-us/windows/security/threat-protection/security-policy-settings/account-lockout-policy");
    private static readonly FindingReference MsDelegation = new("Microsoft: Kerberos constrained delegation",
        "https://learn.microsoft.com/en-us/windows-server/security/kerberos/kerberos-constrained-delegation-overview");
    private static readonly FindingReference MsProtectedAccounts = new("Microsoft: Protected accounts and groups",
        "https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/plan/security-best-practices/appendix-c--protected-accounts-and-groups-in-active-directory");
    private static readonly FindingReference MsGmsa = new("Microsoft: Group Managed Service Accounts",
        "https://learn.microsoft.com/en-us/windows-server/security/group-managed-service-accounts/group-managed-service-accounts-overview");
    private static readonly FindingReference MsLifecycle = new("Microsoft: Product lifecycle",
        "https://learn.microsoft.com/en-us/lifecycle/products/");
    private static readonly FindingReference MsRecycleBin = new("Microsoft: AD Recycle Bin",
        "https://learn.microsoft.com/en-us/windows-server/identity/ad-ds/get-started/adac/introduction-to-active-directory-administrative-center-enhancements--level-100-#ad_recycle_bin_mgmt");

    private static FindingReference Mitre(string id, string name) =>
        new($"MITRE ATT&CK {id} — {name}", $"https://attack.mitre.org/techniques/{id.Replace('.', '/')}/");

    // ── Script builders ───────────────────────────────────────────────────────
    /// <summary>
    /// PowerShell that computes the same "admin" set as the scanner: enabled, nested members of the Tier 0
    /// groups (by SID, plus DnsAdmins by name) and accounts whose primary group is Domain Admins.
    /// </summary>
    private const string Tier0Preamble =
        "Import-Module ActiveDirectory\n" +
        "$sid = (Get-ADDomain).DomainSID.Value   # Enterprise/Schema/Enterprise Key Admins: use the forest root SID in child domains\n" +
        "$t0 = \"$sid-512\",\"$sid-518\",\"$sid-519\",\"$sid-526\",\"$sid-527\",\"$sid-517\",'S-1-5-32-544','S-1-5-32-548','S-1-5-32-549','S-1-5-32-550','S-1-5-32-551' |\n" +
        "    ForEach-Object { Get-ADGroup -Identity $_ -ErrorAction SilentlyContinue }\n" +
        "$t0 += Get-ADGroup -Filter \"Name -eq 'DnsAdmins'\" -ErrorAction SilentlyContinue\n" +
        "$members = $t0 | Get-ADGroupMember -Recursive | Where-Object objectClass -eq 'user'\n" +
        "$members += Get-ADUser -LDAPFilter '(primaryGroupID=512)'\n" +
        "$admins = $members | Sort-Object DistinguishedName -Unique |\n" +
        "    Get-ADUser -Properties userAccountControl, servicePrincipalName, PasswordLastSet, PasswordNeverExpires | Where-Object Enabled\n";

    private static string Cutoff(int days) => $"$cutoff = (Get-Date).AddDays(-{days}).ToFileTimeUtc()";

    /// <summary>Back up the targets, then apply with -WhatIf (remove it to really change AD).</summary>
    private static string ObjectFix(string ruleId, string getCmd, string filter, string apply,
                                    string? preamble = null, string properties = "", string backupColumns = "DistinguishedName, SamAccountName")
    {
        var lines = new List<string> { Ps };
        if (preamble is not null) lines.Add(preamble);
        lines.Add($"$targets = {getCmd} -LDAPFilter \"{filter}\"{(properties.Length > 0 ? " -Properties " + properties : "")}");
        lines.Add($"$targets | Select-Object {backupColumns} | Export-Csv .\\dirhealth-{ruleId}-backup.csv -NoTypeInformation");
        lines.Add($"$targets | {apply} -WhatIf   # review the output, then remove -WhatIf");
        return string.Join("\n", lines);
    }

    private static string ObjectRevert(string ruleId, string perRow) =>
        $"{Ps}\nImport-Csv .\\dirhealth-{ruleId}-backup.csv | ForEach-Object {{ {perRow} }}";

    private static string PolicyFix(string ruleId, string setArgs) =>
        $"{Ps}\nGet-ADDefaultDomainPasswordPolicy | Export-Clixml .\\dirhealth-{ruleId}-backup.xml\n" +
        $"Set-ADDefaultDomainPasswordPolicy -Identity (Get-ADDomain) {setArgs} -WhatIf   # remove -WhatIf to apply";

    private static string PolicyRevert(string ruleId, string setArgs) =>
        $"{Ps}\n$b = Import-Clixml .\\dirhealth-{ruleId}-backup.xml\n" +
        $"Set-ADDefaultDomainPasswordPolicy -Identity (Get-ADDomain) {setArgs}";

    private const string PolicyGpoRisk =
        " If the Default Domain Policy GPO sets a different value, the GPO wins at the next refresh — change it there instead.";

    private static readonly string EolRegex =
        string.Join("|", EolMatcher.Substrings.Select(Regex.Escape));
    private static readonly string EolExemptRegex =
        "Windows 10.*(" + string.Join("|", EolMatcher.Windows10Exemptions) + ")";

    /// <summary>
    /// Scoring table (ADR 0002) — saturations taken over from the v1 formula so typical domains
    /// keep their relative order. Changing a value changes the score: bump ScoreModel.FormulaVersion.
    /// </summary>
    private static readonly Dictionary<string, (ScoreMeasure M, double Sat, ScorePopulation Pop)> Scoring = new()
    {
        ["InactiveUsers"]                    = (ScoreMeasure.Share, 0.30, ScorePopulation.Users),
        ["PasswordNeverExpires"]             = (ScoreMeasure.Share, 0.50, ScorePopulation.Users),
        ["ExpiredPasswords"]                 = (ScoreMeasure.Share, 0.30, ScorePopulation.Users),
        ["EmptyGroups"]                      = (ScoreMeasure.Share, 0.35, ScorePopulation.Groups),
        ["SingleMemberGroups"]               = (ScoreMeasure.Share, 0.35, ScorePopulation.Groups),
        ["InactiveComputers"]                = (ScoreMeasure.Share, 0.40, ScorePopulation.Computers),
        ["ComputersWithoutOS"]               = (ScoreMeasure.Count, 5,    ScorePopulation.None),
        ["EolOperatingSystems"]              = (ScoreMeasure.Count, 5,    ScorePopulation.None),   // a DC counts 3×
        ["KerberoastableAccounts"]           = (ScoreMeasure.Count, 3,    ScorePopulation.None),
        ["AsRepRoasting"]                    = (ScoreMeasure.Count, 5,    ScorePopulation.None),
        ["UnconstrainedDelegationComputers"] = (ScoreMeasure.Count, 1,    ScorePopulation.None),
        ["UnconstrainedDelegationUsers"]     = (ScoreMeasure.Count, 1,    ScorePopulation.None),
        ["PasswordNotRequired"]              = (ScoreMeasure.Count, 5,    ScorePopulation.None),
        ["StaleDomainAdmins"]                = (ScoreMeasure.Count, 4,    ScorePopulation.None),
        ["SidHistory"]                       = (ScoreMeasure.Count, 4,    ScorePopulation.None),
        ["AdminSdHolderAccounts"]            = (ScoreMeasure.Informational, 1, ScorePopulation.None),
        ["WeakPasswordLength"]               = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["WeakPasswordHistory"]              = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["NoAccountLockout"]                 = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["FineGrainedPasswordPolicy"]        = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        // Sprint 19 (formula v3)
        ["HiddenPrimaryGroup"]               = (ScoreMeasure.Count, 1, ScorePopulation.None),
        ["AdminNotProtected"]                = (ScoreMeasure.Count, 3, ScorePopulation.None),
        ["AdminKerberoastable"]              = (ScoreMeasure.Count, 1, ScorePopulation.None),
        ["AdminNoPreauth"]                   = (ScoreMeasure.Count, 1, ScorePopulation.None),
        ["AdminOldPassword"]                 = (ScoreMeasure.Count, 3, ScorePopulation.None),
        ["AdminWithMailbox"]                 = (ScoreMeasure.Count, 3, ScorePopulation.None),
        ["OperatorGroupsNotEmpty"]           = (ScoreMeasure.Count, 3, ScorePopulation.None),
        ["KrbtgtPasswordOld"]                = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["GuestEnabled"]                     = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["MachineAccountQuota"]              = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["PreWindows2000Access"]             = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["RecycleBinDisabled"]               = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["FunctionalLevelOld"]               = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["SingleDomainController"]           = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["DesOnly"]                          = (ScoreMeasure.Count, 1, ScorePopulation.None),
        ["ServiceAccountsWithoutAes"]        = (ScoreMeasure.Count, 5, ScorePopulation.None),
        ["ReversibleEncryption"]             = (ScoreMeasure.Count, 1, ScorePopulation.None),
        ["UserPasswordAttribute"]            = (ScoreMeasure.Count, 1, ScorePopulation.None),
        ["DelegationToDomainControllers"]    = (ScoreMeasure.Count, 1, ScorePopulation.None),
        ["DelegationToMissingAccounts"]      = (ScoreMeasure.Count, 3, ScorePopulation.None),
        ["TrustWithoutSidFiltering"]         = (ScoreMeasure.Count, 1, ScorePopulation.None),
        ["TrustTgtDelegation"]               = (ScoreMeasure.Count, 1, ScorePopulation.None),
        ["TrustInactive"]                    = (ScoreMeasure.Count, 3, ScorePopulation.None),
        // Sprint 20 (formula v4)
        ["LapsNotDeployed"]                  = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["ComputersWithoutLaps"]             = (ScoreMeasure.Share, 0.30, ScorePopulation.Computers),
        ["MachinePasswordNotRotated"]        = (ScoreMeasure.Count, 5, ScorePopulation.None),
        ["DuplicateObjects"]                 = (ScoreMeasure.Count, 10, ScorePopulation.None),
        ["AnonymousLdapAllowed"]             = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["AdminSdExMask"]                    = (ScoreMeasure.Presence, 1, ScorePopulation.None),
        ["SmartcardOldPassword"]             = (ScoreMeasure.Count, 3, ScorePopulation.None),
    };

    /// <summary>Object rules whose objects have no SID (trusts) — keyed by name like policy rules (ADR 0001).</summary>
    public static readonly IReadOnlySet<string> NameKeyed = new HashSet<string>
        { "TrustWithoutSidFiltering", "TrustTgtDelegation", "TrustInactive" };

    private static readonly IReadOnlyDictionary<string, FindingRule> Rules = Build()
        .Select(r => Scoring.TryGetValue(r.Category, out var s)
            ? r with { Measure = s.M, Saturation = s.Sat, Population = s.Pop }
            : r)
        .ToDictionary(r => r.Category);

    public static IReadOnlyCollection<FindingRule> All => (IReadOnlyCollection<FindingRule>)Rules.Values;

    /// <summary>Rule for a category; null for an unknown category (e.g. a newer cache).</summary>
    public static FindingRule? Get(string category) => Rules.TryGetValue(category, out var r) ? r : null;

    private static IEnumerable<FindingRule> Build()
    {
        // ── Users ────────────────────────────────────────────────────────────
        yield return new("InactiveUsers", "DH-USR-001", 3,
            $"Enabled user accounts without a logon in the last {CheckFilters.InactiveUserDays} days (replicated lastLogonTimestamp, ±14 days).",
            "Unused accounts keep their permissions but nobody watches them — a password guessed or leaked later goes unnoticed.",
            "Disable accounts unused for 90+ days, then delete after a retention window. Automate with a scheduled review of lastLogonTimestamp.",
            [MsSecuringAd, Mitre("T1078.002", "Valid Accounts: Domain Accounts")],
            ObjectFix("DH-USR-001", "Get-ADUser", CheckFilters.InactiveUsers("$cutoff"), "Disable-ADAccount", Cutoff(CheckFilters.InactiveUserDays)),
            "Accounts that are used rarely (service, break-glass, seasonal staff) stop working until re-enabled.",
            ObjectRevert("DH-USR-001", "Enable-ADAccount -Identity $_.DistinguishedName"),
            CheckFilters.InactiveUsers("$cutoff"));

        yield return new("PasswordNeverExpires", "DH-USR-002", 4,
            "Enabled user accounts with DONT_EXPIRE_PASSWORD set.",
            "A password that never changes stays valid after it leaks, is phished or cracked — indefinitely.",
            "Remove the 'Password never expires' flag on interactive accounts; where a non-expiring secret is unavoidable (service accounts), rotate it on a fixed schedule or migrate to a gMSA.",
            [MsPasswordPolicy, MsGmsa],
            ObjectFix("DH-USR-002", "Get-ADUser", CheckFilters.PasswordNeverExpires, "Set-ADUser -PasswordNeverExpires $false"),
            "Passwords start expiring under the domain policy; service accounts using these credentials break when the password expires.",
            ObjectRevert("DH-USR-002", "Set-ADUser -Identity $_.DistinguishedName -PasswordNeverExpires $true"),
            CheckFilters.PasswordNeverExpires);

        yield return new("ExpiredPasswords", "DH-USR-003", 4,
            $"Enabled user accounts whose password was last set more than {CheckFilters.OldPasswordDays} days ago.",
            "Old passwords have had years to appear in breaches and wordlists; they are the first thing password spraying hits.",
            "Force a password change at next logon for these accounts, or disable them if the owner has left.",
            [MsPasswordPolicy, Mitre("T1110", "Brute Force")],
            ObjectFix("DH-USR-003", "Get-ADUser", CheckFilters.ExpiredPasswords("$cutoff"), "Set-ADUser -ChangePasswordAtLogon $true", Cutoff(CheckFilters.OldPasswordDays)),
            "Users must change the password at next logon; service accounts cannot and stop working. Accounts with 'password never expires' reject this setting.",
            ObjectRevert("DH-USR-003", "Set-ADUser -Identity $_.DistinguishedName -ChangePasswordAtLogon $false   # sets pwdLastSet to now, not to the old date"),
            CheckFilters.ExpiredPasswords("$cutoff"));

        yield return new("PasswordNotRequired", "DH-USR-004", 7,
            "Enabled user accounts with PASSWD_NOTREQD — AD accepts an empty password for them.",
            "An account that may have an empty password can be logged on to with no password at all.",
            "Clear the PASSWD_NOTREQD flag and enforce a strong password on each account so empty passwords are rejected.",
            [MsPasswordPolicy, Mitre("T1078.002", "Valid Accounts: Domain Accounts")],
            ObjectFix("DH-USR-004", "Get-ADUser", CheckFilters.PasswordNotRequired, "Set-ADAccountControl -PasswordNotRequired $false"),
            "An account that currently has an empty password cannot log on until a password is set.",
            ObjectRevert("DH-USR-004", "Set-ADAccountControl -Identity $_.DistinguishedName -PasswordNotRequired $true"),
            CheckFilters.PasswordNotRequired);

        yield return new("SidHistory", "DH-USR-005", 6,
            "Enabled user accounts that carry SIDs from a previous domain (sIDHistory).",
            "Windows honours historical SIDs in every access check — an account can hold privileges of its old domain without being in any privileged group.",
            "After confirming migrations are complete, clear sIDHistory so migrated accounts no longer carry hidden privileges from the source domain.",
            [Mitre("T1134.005", "Access Token Manipulation: SID-History Injection")],
            $"{Ps}\n$targets = Get-ADUser -LDAPFilter \"{CheckFilters.SidHistory}\" -Properties sIDHistory\n" +
            "$targets | Select-Object DistinguishedName, SamAccountName, @{n='sIDHistory';e={$_.sIDHistory -join ';'}} | Export-Csv .\\dirhealth-DH-USR-005-backup.csv -NoTypeInformation\n" +
            "$targets | ForEach-Object { Set-ADUser $_ -Remove @{sIDHistory = $_.sIDHistory} -WhatIf }   # review the output, then remove -WhatIf",
            "Resources in the old domain that still grant access only to the historical SID become unreachable for these users.",
            "# sIDHistory cannot be written back with PowerShell — only DsAddSidHistory/ADMT during a migration can set it.\n" +
            "# To undo, restore the user objects from a system-state backup (authoritative restore). The backup CSV lists the removed SIDs.",
            CheckFilters.SidHistory);

        // ── Groups ───────────────────────────────────────────────────────────
        yield return new("EmptyGroups", "DH-GRP-001", 1,
            "Groups without any member.",
            "Unused groups clutter permission reviews and can be refilled unnoticed by anyone allowed to manage them.",
            "Delete groups that serve no purpose. Empty groups clutter delegation reviews and can be silently repopulated by an attacker.",
            [MsSecuringAd, MsRecycleBin],
            ObjectFix("DH-GRP-001", "Get-ADGroup", CheckFilters.EmptyGroups, "Remove-ADGroup -Confirm:$false", backupColumns: "DistinguishedName, Name, SID"),
            "A group may still be referenced in ACLs, GPO filters or applications even without members; deleting it removes those grants.",
            ObjectRevert("DH-GRP-001", "Get-ADObject -IncludeDeletedObjects -Filter \"isDeleted -eq `$true -and Name -like '$($_.Name)*'\" | Restore-ADObject   # needs the AD Recycle Bin"),
            CheckFilters.EmptyGroups);

        yield return new("SingleMemberGroups", "DH-GRP-002", 1,
            "Groups with exactly one member.",
            "One-off groups make the delegation model hard to read; permissions end up granted in places nobody reviews.",
            "Review whether the group is still needed; consolidate one-off groups to keep the delegation model legible.",
            [MsSecuringAd],
            $"{Ps}\nGet-ADGroup -LDAPFilter \"{CheckFilters.AllGroups}\" -Properties member |\n" +
            "    Where-Object { $_.member.Count -eq 1 } |\n" +
            "    Select-Object Name, @{n='Member';e={$_.member}}   # read-only list for the review",
            "None — this only lists the groups for review.",
            "# Read-only listing — nothing to undo.",
            CheckFilters.AllGroups);

        // ── Computers ────────────────────────────────────────────────────────
        yield return new("InactiveComputers", "DH-CMP-001", 2,
            $"Enabled computer accounts without a logon in the last {CheckFilters.InactiveComputerDays} days.",
            "Stale computer accounts keep their machine password and group memberships — a foothold for lateral movement if the host comes back or the account is reused.",
            "Disable then delete stale computer accounts after confirming the host is decommissioned; stale accounts are a lateral-movement foothold.",
            [MsSecuringAd],
            ObjectFix("DH-CMP-001", "Get-ADComputer", CheckFilters.InactiveComputers("$cutoff"), "Disable-ADAccount", Cutoff(CheckFilters.InactiveComputerDays)),
            "A machine that is merely offline (laptop, standby server) cannot authenticate to the domain until re-enabled.",
            ObjectRevert("DH-CMP-001", "Enable-ADAccount -Identity $_.DistinguishedName"),
            CheckFilters.InactiveComputers("$cutoff"));

        yield return new("ComputersWithoutOS", "DH-CMP-002", 1,
            "Computer objects without an operatingSystem value.",
            "Often never-joined, pre-staged or non-Windows objects — nobody owns them, so nobody notices if they are misused.",
            "Investigate why the operatingSystem attribute is empty (often non-Windows or never-joined objects) and remove obsolete entries.",
            [MsSecuringAd],
            $"{Ps}\nGet-ADComputer -LDAPFilter \"{CheckFilters.ComputersWithoutOs}\" -Properties whenCreated, lastLogonTimestamp |\n" +
            "    Select-Object Name, DistinguishedName, whenCreated, @{n='LastLogon';e={[DateTime]::FromFileTimeUtc($_.lastLogonTimestamp)}}   # read-only list",
            "None — this only lists the objects for review.",
            "# Read-only listing — nothing to undo.",
            CheckFilters.ComputersWithoutOs);

        yield return new("EolOperatingSystems", "DH-CMP-003", 7,
            "Computers running a Windows version past its end of support.",
            "No more security updates: every vulnerability found from now on stays open for good. On a domain controller this endangers the whole domain.",
            "Upgrade or replace end-of-life systems immediately; isolate any that cannot be upgraded, and prioritise domain controllers. " +
            "Systems covered by paid Extended Security Updates (ESU) still receive patches — DirHealth cannot see ESU enrolment in AD, so verify those manually.",
            [MsLifecycle],
            $"{Ps}\nGet-ADComputer -LDAPFilter \"{CheckFilters.ComputersWithOs}\" -Properties operatingSystem |\n" +
            $"    Where-Object {{ $_.operatingSystem -match '{EolRegex}' -and $_.operatingSystem -notmatch '{EolExemptRegex}' }} |\n" +
            "    Select-Object Name, operatingSystem   # read-only list — the fix is an OS upgrade",
            "None — this only lists the systems; upgrading them is a project, not a command.",
            "# Read-only listing — nothing to undo.",
            CheckFilters.ComputersWithOs);

        // ── Kerberos ─────────────────────────────────────────────────────────
        yield return new("KerberoastableAccounts", "DH-KRB-001", 8,
            "Enabled user accounts with a servicePrincipalName (krbtgt excluded).",
            "Any domain user can request a service ticket for these accounts and crack its password offline, unnoticed.",
            "Give service accounts long (25+ char) random passwords or migrate to gMSAs so the extractable TGS hash cannot be cracked offline.",
            [MsGmsa, Mitre("T1558.003", "Steal or Forge Kerberos Tickets: Kerberoasting")],
            ObjectFix("DH-KRB-001", "Get-ADUser", CheckFilters.Kerberoastable, "Set-ADUser -KerberosEncryptionType AES128,AES256",
                properties: "msDS-SupportedEncryptionTypes", backupColumns: "DistinguishedName, SamAccountName, msDS-SupportedEncryptionTypes") +
            "\n# AES-only tickets slow cracking down; the real fix is a gMSA or a 25+ character random password.",
            "Clients or services that only speak RC4 can no longer get tickets for these accounts. Reset the password afterwards so AES keys exist.",
            ObjectRevert("DH-KRB-001",
                "if ($_.'msDS-SupportedEncryptionTypes') { Set-ADUser -Identity $_.DistinguishedName -Replace @{'msDS-SupportedEncryptionTypes' = [int]$_.'msDS-SupportedEncryptionTypes'} } " +
                "else { Set-ADUser -Identity $_.DistinguishedName -Clear 'msDS-SupportedEncryptionTypes' }"),
            CheckFilters.Kerberoastable);

        yield return new("AsRepRoasting", "DH-KRB-002", 9,
            "Enabled user accounts with Kerberos pre-authentication disabled (DONT_REQ_PREAUTH).",
            "Anyone on the network — without any credentials — can request encrypted material for these accounts and crack the password offline.",
            "Re-enable Kerberos pre-authentication (clear DONT_REQUIRE_PREAUTH) on these accounts and give them strong passwords.",
            [Mitre("T1558.004", "Steal or Forge Kerberos Tickets: AS-REP Roasting")],
            ObjectFix("DH-KRB-002", "Get-ADUser", CheckFilters.AsRepRoastable, "Set-ADAccountControl -DoesNotRequirePreAuth $false"),
            "Old Kerberos clients (some Unix/Java stacks) that cannot pre-authenticate fail to log on with these accounts.",
            ObjectRevert("DH-KRB-002", "Set-ADAccountControl -Identity $_.DistinguishedName -DoesNotRequirePreAuth $true"),
            CheckFilters.AsRepRoastable);

        yield return new("UnconstrainedDelegationComputers", "DH-KRB-003", 10,
            "Computers other than domain controllers trusted for unconstrained Kerberos delegation.",
            "Every user who authenticates to such a host leaves a reusable ticket (TGT) on it — whoever controls the host can impersonate them, Domain Admins included.",
            "Remove unconstrained delegation from non-DC computers; use resource-based constrained delegation instead, and mark sensitive accounts as 'not delegated'.",
            [MsDelegation, Mitre("T1558", "Steal or Forge Kerberos Tickets")],
            ObjectFix("DH-KRB-003", "Get-ADComputer", CheckFilters.UnconstrainedDelegationComputers, "Set-ADAccountControl -TrustedForDelegation $false",
                backupColumns: "DistinguishedName, Name"),
            "Applications that rely on delegation through this host (e.g. web front ends to SQL) stop forwarding user credentials until constrained delegation is configured.",
            ObjectRevert("DH-KRB-003", "Set-ADAccountControl -Identity $_.DistinguishedName -TrustedForDelegation $true"),
            CheckFilters.UnconstrainedDelegationComputers);

        yield return new("UnconstrainedDelegationUsers", "DH-KRB-004", 8,
            "Enabled user accounts trusted for unconstrained Kerberos delegation.",
            "Services running under these accounts receive reusable tickets of every connecting user and can impersonate them anywhere in the domain.",
            "Remove the TRUSTED_FOR_DELEGATION flag from user accounts; user accounts should almost never be trusted for delegation.",
            [MsDelegation, Mitre("T1558", "Steal or Forge Kerberos Tickets")],
            ObjectFix("DH-KRB-004", "Get-ADUser", CheckFilters.UnconstrainedDelegationUsers, "Set-ADAccountControl -TrustedForDelegation $false"),
            "Services running as these accounts stop forwarding user credentials until constrained delegation is configured.",
            ObjectRevert("DH-KRB-004", "Set-ADAccountControl -Identity $_.DistinguishedName -TrustedForDelegation $true"),
            CheckFilters.UnconstrainedDelegationUsers);

        // ── Privileged access ────────────────────────────────────────────────
        yield return new("AdminSdHolderAccounts", "DH-PRV-001", 4,
            "Enabled user accounts with adminCount=1 — current or former members of a protected group.",
            "Former admins keep the hardened, non-inheriting ACL and are easy to overlook; current ones need regular review.",
            "Confirm each adminCount=1 account still needs elevated rights; for removed admins, clear adminCount and re-enable inheritance on the object ACL.",
            [MsProtectedAccounts],
            $"{Ps}\n$targets = Get-ADUser -LDAPFilter \"{CheckFilters.AdminSdHolder}\"\n" +
            "$targets | Select-Object DistinguishedName, SamAccountName | Export-Csv .\\dirhealth-DH-PRV-001-backup.csv -NoTypeInformation\n" +
            "# Pick only accounts that are no longer in a protected group:\n" +
            "$targets | Out-GridView -PassThru -Title 'Former admins only' | Set-ADUser -Clear adminCount -WhatIf   # remove -WhatIf to apply",
            "Clearing adminCount does not restore ACL inheritance — re-enable it on the object separately. If the account is still in a protected group, SDProp sets adminCount back within 60 minutes.",
            ObjectRevert("DH-PRV-001", "Set-ADUser -Identity $_.DistinguishedName -Replace @{adminCount = 1}"),
            CheckFilters.AdminSdHolder);

        var staleFilter = CheckFilters.StaleDomainAdmins("$dn", "$cutoff");
        yield return new("StaleDomainAdmins", "DH-PRV-002", 7,
            $"Enabled members of Domain Admins (nested and primary group included) without a logon in the last {CheckFilters.StaleAdminDays} days.",
            "Dormant admin accounts are rarely monitored and their passwords rarely rotated — a perfect target with full domain control.",
            "Remove dormant accounts from Domain Admins, or if still required, rotate their passwords and enable strict monitoring.",
            [MsSecuringAd, Mitre("T1078.002", "Valid Accounts: Domain Accounts")],
            $"{Ps}\n{Cutoff(CheckFilters.StaleAdminDays)}\n" +
            "$da = Get-ADGroup -Identity \"$((Get-ADDomain).DomainSID.Value)-512\"   # by SID — the name is localized\n" +
            "$dn = $da.DistinguishedName -replace '\\\\','\\5c' -replace '\\(','\\28' -replace '\\)','\\29'\n" +
            $"$targets = Get-ADUser -LDAPFilter \"{staleFilter}\"\n" +
            "$targets | Select-Object DistinguishedName, SamAccountName | Export-Csv .\\dirhealth-DH-PRV-002-backup.csv -NoTypeInformation\n" +
            "$targets | ForEach-Object { Remove-ADGroupMember -Identity $da -Members $_ -Confirm:$false -WhatIf }   # remove -WhatIf to apply",
            "Only direct members are removed. Nested members must be taken out of the intermediate group; a primary-group membership (primaryGroupID 512) has to be changed on the account.",
            $"{Ps}\n$da = Get-ADGroup -Identity \"$((Get-ADDomain).DomainSID.Value)-512\"\n" +
            "Import-Csv .\\dirhealth-DH-PRV-002-backup.csv | ForEach-Object { Add-ADGroupMember -Identity $da -Members $_.DistinguishedName }",
            staleFilter);

        // ── Domain password policy (domain head attributes, no search filter) ─
        yield return new("WeakPasswordLength", "DH-POL-001", 6,
            "The default domain password policy allows passwords shorter than 12 characters.",
            "Short passwords fall to guessing and offline cracking in hours.",
            "Raise the domain minimum password length to at least 12–14 characters via the Default Domain Policy.",
            [MsPasswordPolicy, Mitre("T1110", "Brute Force")],
            PolicyFix("DH-POL-001", "-MinPasswordLength 12"),
            "Takes effect at each user's next password change; scripts that set short passwords start failing." + PolicyGpoRisk,
            PolicyRevert("DH-POL-001", "-MinPasswordLength $b.MinPasswordLength"),
            null);

        yield return new("WeakPasswordHistory", "DH-POL-002", 4,
            "The default domain password policy remembers fewer than 10 previous passwords.",
            "Users can cycle back to a password that was already exposed.",
            "Increase enforced password history to 10 or more so users cannot cycle back to a recent password.",
            [MsPasswordPolicy],
            PolicyFix("DH-POL-002", "-PasswordHistoryCount 24"),
            "Users can no longer reuse recent passwords; expect more help-desk resets for a while." + PolicyGpoRisk,
            PolicyRevert("DH-POL-002", "-PasswordHistoryCount $b.PasswordHistoryCount"),
            null);

        yield return new("NoAccountLockout", "DH-POL-003", 6,
            "The default domain password policy has no account lockout threshold.",
            "Attackers can guess passwords online without limit.",
            "Configure an account-lockout threshold (e.g. 10 attempts / 15 min) to blunt online brute-force attacks.",
            [MsLockout, Mitre("T1110", "Brute Force")],
            PolicyFix("DH-POL-003", "-LockoutThreshold 10 -LockoutDuration 00:15:00 -LockoutObservationWindow 00:15:00"),
            "Users and services with a stale cached password get locked out for 15 minutes; an attacker can lock accounts out on purpose." + PolicyGpoRisk,
            PolicyRevert("DH-POL-003", "-LockoutThreshold $b.LockoutThreshold -LockoutDuration $b.LockoutDuration -LockoutObservationWindow $b.LockoutObservationWindow"),
            null);

        yield return new("FineGrainedPasswordPolicy", "DH-POL-004", 5,
            "A Password Settings Object (fine-grained policy) is weaker than the baseline: reversible encryption, under 12 characters, or no lockout.",
            "The weaker PSO silently overrides the domain policy for everyone it applies to.",
            "Tighten the Password Settings Object to meet or exceed the domain baseline; never store passwords with reversible encryption.",
            [MsPasswordPolicy],
            $"{Ps}\n$psos = Get-ADFineGrainedPasswordPolicy -Filter *\n" +
            "$psos | Export-Clixml .\\dirhealth-DH-POL-004-backup.xml\n" +
            "$psos | Where-Object ReversibleEncryptionEnabled     | Set-ADFineGrainedPasswordPolicy -ReversibleEncryptionEnabled $false -WhatIf\n" +
            "$psos | Where-Object { $_.MinPasswordLength -lt 12 } | Set-ADFineGrainedPasswordPolicy -MinPasswordLength 12 -WhatIf\n" +
            "$psos | Where-Object { $_.LockoutThreshold -eq 0 }   | Set-ADFineGrainedPasswordPolicy -LockoutThreshold 10 -WhatIf   # remove -WhatIf to apply",
            "Affected users must meet the stronger rules at their next password change; reversible encryption off breaks apps that read cleartext passwords (e.g. CHAP/Digest).",
            $"{Ps}\nImport-Clixml .\\dirhealth-DH-POL-004-backup.xml | ForEach-Object {{\n" +
            "    Set-ADFineGrainedPasswordPolicy -Identity $_.DistinguishedName -ReversibleEncryptionEnabled $_.ReversibleEncryptionEnabled `\n" +
            "        -MinPasswordLength $_.MinPasswordLength -LockoutThreshold $_.LockoutThreshold }",
            null);

        // ══ Sprint 19 — new LDAP checks ══════════════════════════════════════

        // ── S19-1 Tier 0 / hidden primary group ──────────────────────────────
        yield return new("HiddenPrimaryGroup", "DH-PRV-003", 8,
            "Enabled accounts whose primary group is not the default (Domain Users/Guests for users; Domain Computers/Controllers/RODCs for computers).",
            "Primary-group membership is not stored in member/memberOf — an account can sit in Domain Admins without appearing in the group's member list.",
            "Set the primary group back to the default and, if the account really needs the group, add it as a visible regular member.",
            [MsProtectedAccounts],
            ObjectFix("DH-PRV-003", "Get-ADUser", CheckFilters.HiddenPrimaryGroupUsers, "Set-ADUser -Replace @{primaryGroupID = 513}",
                properties: "primaryGroupID", backupColumns: "DistinguishedName, SamAccountName, primaryGroupID") +
            "\n# Computers (same rule, default 515):\n" +
            $"Get-ADComputer -LDAPFilter \"{CheckFilters.HiddenPrimaryGroupComputers}\" -Properties primaryGroupID | Select-Object DistinguishedName, primaryGroupID | Export-Csv .\\dirhealth-DH-PRV-003-computers-backup.csv -NoTypeInformation\n" +
            $"Get-ADComputer -LDAPFilter \"{CheckFilters.HiddenPrimaryGroupComputers}\" | Set-ADComputer -Replace @{{primaryGroupID = 515}} -WhatIf",
            "The account must be a member of Domain Users (or Domain Computers) for the change to succeed; it loses the rights it got through the hidden group.",
            ObjectRevert("DH-PRV-003", "Set-ADObject -Identity $_.DistinguishedName -Replace @{primaryGroupID = [int]$_.primaryGroupID}") +
            "\nImport-Csv .\\dirhealth-DH-PRV-003-computers-backup.csv | ForEach-Object { Set-ADObject -Identity $_.DistinguishedName -Replace @{primaryGroupID = [int]$_.primaryGroupID} }",
            CheckFilters.HiddenPrimaryGroupUsers);

        // ── S19-2 admin accounts (Tier 0 = BloodHound high value; by SID) ────
        yield return new("AdminNotProtected", "DH-PRV-004", 7,
            "Enabled Tier 0 accounts that are neither \"sensitive and cannot be delegated\" nor members of Protected Users.",
            "Their credentials can be forwarded by any server they log on to — one compromised server with delegation becomes a domain admin.",
            "Mark admin accounts as \"Account is sensitive and cannot be delegated\" and add them to Protected Users after testing.",
            [MsProtectedAccounts, Mitre("T1558", "Steal or Forge Kerberos Tickets")],
            Tier0Preamble +
            "$pu = Get-ADGroupMember -Identity \"$sid-525\" -Recursive | ForEach-Object DistinguishedName   # Protected Users\n" +
            "$targets = $admins | Where-Object { -not ($_.userAccountControl -band 0x100000) -and $pu -notcontains $_.DistinguishedName }\n" +
            "$targets | Select-Object DistinguishedName, SamAccountName | Export-Csv .\\dirhealth-DH-PRV-004-backup.csv -NoTypeInformation\n" +
            "$targets | Set-ADAccountControl -AccountNotDelegated $true -WhatIf   # review the output, then remove -WhatIf",
            "Delegation-dependent admin tasks (e.g. double-hop remoting) stop working for these accounts; Protected Users additionally disables NTLM and cached logons.",
            ObjectRevert("DH-PRV-004", "Set-ADAccountControl -Identity $_.DistinguishedName -AccountNotDelegated $false"),
            null);

        yield return new("AdminKerberoastable", "DH-PRV-005", 10,
            "Enabled Tier 0 accounts with a servicePrincipalName.",
            "Any domain user can request a ticket for them and crack the password offline — a direct path to domain admin.",
            "Never run services as a Tier 0 account: move the service to a gMSA or a dedicated, unprivileged account and remove the SPN.",
            [MsGmsa, Mitre("T1558.003", "Steal or Forge Kerberos Tickets: Kerberoasting")],
            Tier0Preamble +
            "$targets = $admins | Where-Object { $_.servicePrincipalName -and $_.SamAccountName -ne 'krbtgt' }\n" +
            "$targets | Select-Object DistinguishedName, SamAccountName, @{n='SPNs';e={$_.servicePrincipalName -join ';'}} | Export-Csv .\\dirhealth-DH-PRV-005-backup.csv -NoTypeInformation\n" +
            "$targets | ForEach-Object { Set-ADUser $_ -ServicePrincipalNames @{Remove = [string[]]$_.servicePrincipalName} -WhatIf }   # remove -WhatIf to apply",
            "The service that used these SPNs stops authenticating with Kerberos until it runs under another account.",
            ObjectRevert("DH-PRV-005", "Set-ADUser -Identity $_.DistinguishedName -ServicePrincipalNames @{Add = $_.SPNs -split ';'}"),
            null);

        yield return new("AdminNoPreauth", "DH-PRV-006", 10,
            "Enabled Tier 0 accounts with Kerberos pre-authentication disabled.",
            "Anyone on the network can request crackable material for a domain admin password — without any credentials.",
            "Re-enable Kerberos pre-authentication on these accounts immediately and reset their passwords.",
            [Mitre("T1558.004", "Steal or Forge Kerberos Tickets: AS-REP Roasting")],
            Tier0Preamble +
            "$targets = $admins | Where-Object { $_.userAccountControl -band 0x400000 }\n" +
            "$targets | Select-Object DistinguishedName, SamAccountName | Export-Csv .\\dirhealth-DH-PRV-006-backup.csv -NoTypeInformation\n" +
            "$targets | Set-ADAccountControl -DoesNotRequirePreAuth $false -WhatIf   # remove -WhatIf to apply",
            "Legacy Kerberos clients that cannot pre-authenticate fail to log on with these accounts.",
            ObjectRevert("DH-PRV-006", "Set-ADAccountControl -Identity $_.DistinguishedName -DoesNotRequirePreAuth $true"),
            null);

        yield return new("AdminOldPassword", "DH-PRV-007", 6,
            $"Enabled Tier 0 accounts whose password is older than {Checks.CheckLogic.AdminPasswordMaxDays} days or never expires.",
            "An admin password that has been valid for years has had years to leak.",
            "Rotate admin passwords at least yearly and remove \"password never expires\" from admin accounts.",
            [MsPasswordPolicy, Mitre("T1078.002", "Valid Accounts: Domain Accounts")],
            Tier0Preamble +
            $"$limit = (Get-Date).AddDays(-{Checks.CheckLogic.AdminPasswordMaxDays})\n" +
            "$targets = $admins | Where-Object { $_.PasswordNeverExpires -or -not $_.PasswordLastSet -or $_.PasswordLastSet -lt $limit }\n" +
            "$targets | Select-Object DistinguishedName, SamAccountName, PasswordNeverExpires | Export-Csv .\\dirhealth-DH-PRV-007-backup.csv -NoTypeInformation\n" +
            "$targets | Set-ADUser -PasswordNeverExpires $false -ChangePasswordAtLogon $true -WhatIf   # remove -WhatIf to apply",
            "Admins must set a new password at next logon; scheduled tasks or services running as these accounts break until updated.",
            ObjectRevert("DH-PRV-007", "Set-ADUser -Identity $_.DistinguishedName -ChangePasswordAtLogon $false -PasswordNeverExpires ([bool]::Parse($_.PasswordNeverExpires))"),
            null);

        yield return new("AdminWithMailbox", "DH-PRV-008", 4,
            "Enabled Tier 0 accounts that have an Exchange mailbox.",
            "Mail is the most common way in for attackers — a phishing mail opened by a domain admin account compromises the domain.",
            "Give admins a separate, unprivileged account for mail and browsing; remove the mailbox from the admin account (in Exchange).",
            [MsSecuringAd],
            Tier0Preamble +
            "$admins | Get-ADUser -Properties msExchMailboxGuid, homeMDB | Where-Object { $_.msExchMailboxGuid -or $_.homeMDB } |\n" +
            "    Select-Object SamAccountName, DistinguishedName   # read-only list — remove the mailbox in Exchange (Disable-Mailbox)",
            "None from this list; disabling a mailbox in Exchange disconnects it (recoverable within the retention period).",
            "# Read-only listing — nothing to undo.",
            null);

        yield return new("OperatorGroupsNotEmpty", "DH-PRV-009", 6,
            "Members of Account, Server, Print or Backup Operators.",
            "These built-in groups can log on to domain controllers, load drivers or read the AD database — each is a path to domain admin.",
            "Empty the operator groups; delegate the specific tasks with least privilege instead.",
            [MsProtectedAccounts],
            $"{Ps}\n$ops = 'S-1-5-32-548','S-1-5-32-549','S-1-5-32-550','S-1-5-32-551'\n" +
            "$rows = foreach ($g in $ops) { Get-ADGroupMember -Identity $g | Select-Object @{n='Group';e={$g}}, distinguishedName }\n" +
            "$rows | Export-Csv .\\dirhealth-DH-PRV-009-backup.csv -NoTypeInformation\n" +
            "$rows | ForEach-Object { Remove-ADGroupMember -Identity $_.Group -Members $_.distinguishedName -Confirm:$false -WhatIf }   # remove -WhatIf to apply",
            "Whoever relied on the operator rights (backup software, print admins) loses them until a delegated replacement exists.",
            ObjectRevert("DH-PRV-009", "Add-ADGroupMember -Identity $_.Group -Members $_.distinguishedName"),
            null);

        // ── S19-3 krbtgt and domain basics ───────────────────────────────────
        yield return new("KrbtgtPasswordOld", "DH-DOM-001", 8,
            $"The krbtgt account password is older than {Checks.CheckLogic.KrbtgtMaxDays} days.",
            "Anyone who ever stole the krbtgt hash can forge tickets (golden tickets) until the password is reset twice.",
            "Reset the krbtgt password twice, with at least the maximum ticket lifetime (10 hours by default) in between — e.g. with Microsoft's New-KrbtgtKeys.ps1.",
            [Mitre("T1558.001", "Steal or Forge Kerberos Tickets: Golden Ticket")],
            $"{Ps}\nGet-ADUser krbtgt -Properties PasswordLastSet | Select-Object DistinguishedName, PasswordLastSet | Export-Csv .\\dirhealth-DH-DOM-001-backup.csv -NoTypeInformation   # record only\n" +
            "$pw = ConvertTo-SecureString ([guid]::NewGuid().ToString() + [guid]::NewGuid().ToString()) -AsPlainText -Force\n" +
            "Set-ADAccountPassword -Identity krbtgt -Reset -NewPassword $pw -WhatIf   # first reset; repeat after replication and >10 h",
            "Resetting twice within the ticket lifetime invalidates all Kerberos tickets in the domain (mass logon failures). Wait for replication between resets.",
            "# A password reset cannot be undone. Do not do the second reset before all DCs replicated and the ticket lifetime passed.",
            null);

        yield return new("GuestEnabled", "DH-DOM-002", 7,
            "The built-in Guest account (RID 501) is enabled.",
            "Guest allows anonymous-like access to shares and resources granted to Everyone.",
            "Disable the built-in Guest account.",
            [MsSecuringAd],
            $"{Ps}\n$guest = Get-ADUser -Identity \"$((Get-ADDomain).DomainSID.Value)-501\"   # by SID — the name is localized\n" +
            "$guest | Select-Object DistinguishedName, Enabled | Export-Csv .\\dirhealth-DH-DOM-002-backup.csv -NoTypeInformation\n" +
            "$guest | Disable-ADAccount -WhatIf   # remove -WhatIf to apply",
            "Applications that (unusually) rely on guest access stop working.",
            ObjectRevert("DH-DOM-002", "Enable-ADAccount -Identity $_.DistinguishedName"),
            null);

        yield return new("MachineAccountQuota", "DH-DOM-003", 5,
            "ms-DS-MachineAccountQuota is above 0: every user can join computers to the domain.",
            "A self-created computer account is the starting point of several escalation techniques (e.g. resource-based delegation attacks).",
            "Set ms-DS-MachineAccountQuota to 0 and delegate domain joins to a dedicated group.",
            [MsSecuringAd],
            $"{Ps}\nGet-ADDomain | Get-ADObject -Properties ms-DS-MachineAccountQuota | Select-Object DistinguishedName, ms-DS-MachineAccountQuota | Export-Csv .\\dirhealth-DH-DOM-003-backup.csv -NoTypeInformation\n" +
            "Set-ADDomain -Identity (Get-ADDomain) -Replace @{'ms-DS-MachineAccountQuota' = 0} -WhatIf   # remove -WhatIf to apply",
            "Users without delegated rights can no longer join computers; helpdesk staff need a delegation on the target OUs.",
            ObjectRevert("DH-DOM-003", "Set-ADDomain -Identity (Get-ADDomain) -Replace @{'ms-DS-MachineAccountQuota' = [int]$_.'ms-DS-MachineAccountQuota'}"),
            null);

        yield return new("PreWindows2000Access", "DH-DOM-004", 6,
            "Pre-Windows 2000 Compatible Access contains Authenticated Users, Anonymous Logon or Everyone.",
            "Lets every account — or anonymous sessions — read all user and group attributes, which helps attackers map the domain.",
            "Remove Authenticated Users, Anonymous Logon and Everyone from Pre-Windows 2000 Compatible Access (test legacy NT4/RAS applications first).",
            [MsSecuringAd],
            $"{Ps}\n$group = Get-ADGroup -Identity 'S-1-5-32-554'\n" +
            "$risky = Get-ADGroupMember $group | Where-Object { $_.SID.Value -in 'S-1-5-11','S-1-5-7','S-1-1-0' }\n" +
            "$risky | Select-Object @{n='SID';e={$_.SID.Value}} | Export-Csv .\\dirhealth-DH-DOM-004-backup.csv -NoTypeInformation\n" +
            "$risky | ForEach-Object { Remove-ADGroupMember $group -Members $_ -Confirm:$false -WhatIf }   # remove -WhatIf to apply",
            "Very old applications (NT4-era, some RAS/NAS appliances) that query AD anonymously stop working.",
            $"{Ps}\nImport-Csv .\\dirhealth-DH-DOM-004-backup.csv | ForEach-Object {{ Add-ADGroupMember -Identity 'S-1-5-32-554' -Members ([System.Security.Principal.SecurityIdentifier]$_.SID) }}",
            null);

        yield return new("RecycleBinDisabled", "DH-DOM-005", 3,
            "The AD Recycle Bin is not enabled.",
            "Deleted objects (accidentally or by an attacker) cannot be restored with their attributes and group memberships.",
            "Enable the AD Recycle Bin (forest-wide; requires forest functional level 2008 R2).",
            [MsRecycleBin],
            $"{Ps}\nGet-ADOptionalFeature -Filter * | Export-Clixml .\\dirhealth-DH-DOM-005-backup.xml   # record only\n" +
            "Enable-ADOptionalFeature -Identity 'Recycle Bin Feature' -Scope ForestOrConfigurationSet -Target (Get-ADForest).Name -WhatIf",
            "Irreversible: the Recycle Bin cannot be disabled again. Objects deleted before enabling stay non-restorable.",
            "# Cannot be undone — the AD Recycle Bin stays enabled once turned on.",
            null);

        yield return new("FunctionalLevelOld", "DH-DOM-006", 3,
            "The domain functional level is below Windows Server 2016.",
            "Newer security features (e.g. Protected Users protections, automatic NTLM secret rolling for smart-card users) are not available.",
            "Upgrade all DCs to supported versions, then raise the domain (and forest) functional level.",
            [MsLifecycle],
            $"{Ps}\nGet-ADDomain | Select-Object DNSRoot, DomainMode | Export-Csv .\\dirhealth-DH-DOM-006-backup.csv -NoTypeInformation   # record only\n" +
            "Set-ADDomainMode -Identity (Get-ADDomain) -DomainMode Windows2016Domain -WhatIf   # only after every DC runs 2016 or later",
            "Raising the level is effectively one-way and fails while older DCs exist.",
            "# Lowering the functional level is only possible in narrow cases — treat this as irreversible.",
            null);

        yield return new("SingleDomainController", "DH-DOM-007", 5,
            "The domain has fewer than two domain controllers.",
            "A single DC is a single point of failure — its loss stops logons and, without a tested backup, loses the domain.",
            "Add a second domain controller (ideally in another location) and test system-state backups.",
            [MsSecuringAd],
            $"{Ps}\nGet-ADDomainController -Filter * | Select-Object HostName, Site, OperatingSystem   # read-only list — add a DC with Install-ADDSDomainController",
            "None from this list.",
            "# Read-only listing — nothing to undo.",
            null);

        // ── S19-4 Kerberos encryption ────────────────────────────────────────
        yield return new("DesOnly", "DH-KRB-005", 8,
            "Enabled user or computer accounts restricted to DES Kerberos encryption (USE_DES_KEY_ONLY).",
            "DES is broken: tickets for these accounts can be decrypted and forged in practice.",
            "Clear \"Use only DES encryption\" and reset the account password so AES keys are created.",
            [MsDelegation, Mitre("T1558", "Steal or Forge Kerberos Tickets")],
            ObjectFix("DH-KRB-005", "Get-ADUser", CheckFilters.DesOnlyUsers, "Set-ADAccountControl -UseDESKeyOnly $false") +
            $"\nGet-ADComputer -LDAPFilter \"{CheckFilters.DesOnlyComputers}\" | Set-ADAccountControl -UseDESKeyOnly $false -WhatIf",
            "Applications that only speak DES fail to authenticate against these accounts.",
            ObjectRevert("DH-KRB-005", "Set-ADAccountControl -Identity $_.DistinguishedName -UseDESKeyOnly $true"),
            CheckFilters.DesOnlyUsers);

        yield return new("ServiceAccountsWithoutAes", "DH-KRB-006", 5,
            "Enabled service accounts (with an SPN) whose msDS-SupportedEncryptionTypes is unset or has no AES bit.",
            "Their tickets fall back to RC4, which cracks much faster than AES.",
            "Enable AES128/AES256 on the account and reset its password, or move the service to a gMSA.",
            [MsGmsa, Mitre("T1558.003", "Steal or Forge Kerberos Tickets: Kerberoasting")],
            $"{Ps}\n$targets = Get-ADUser -LDAPFilter \"{CheckFilters.Kerberoastable}\" -Properties msDS-SupportedEncryptionTypes |\n" +
            "    Where-Object { -not ($_.'msDS-SupportedEncryptionTypes' -band 0x18) }\n" +
            "$targets | Select-Object DistinguishedName, SamAccountName, msDS-SupportedEncryptionTypes | Export-Csv .\\dirhealth-DH-KRB-006-backup.csv -NoTypeInformation\n" +
            "$targets | Set-ADUser -KerberosEncryptionType AES128,AES256 -WhatIf   # then reset each password",
            "RC4-only clients can no longer get tickets for these services.",
            ObjectRevert("DH-KRB-006",
                "if ($_.'msDS-SupportedEncryptionTypes') { Set-ADUser -Identity $_.DistinguishedName -Replace @{'msDS-SupportedEncryptionTypes' = [int]$_.'msDS-SupportedEncryptionTypes'} } " +
                "else { Set-ADUser -Identity $_.DistinguishedName -Clear 'msDS-SupportedEncryptionTypes' }"),
            CheckFilters.Kerberoastable);

        yield return new("ReversibleEncryption", "DH-KRB-007", 8,
            "Enabled accounts allowed to store their password with reversible encryption.",
            "The password is stored in a form that can be turned back into plain text by anyone who reads the AD database.",
            "Clear \"Store password using reversible encryption\" and reset the password so the reversible copy is removed.",
            [MsPasswordPolicy],
            ObjectFix("DH-KRB-007", "Get-ADUser", CheckFilters.ReversibleUsers, "Set-ADAccountControl -AllowReversiblePasswordEncryption $false") +
            $"\nGet-ADComputer -LDAPFilter \"{CheckFilters.ReversibleComputers}\" | Set-ADAccountControl -AllowReversiblePasswordEncryption $false -WhatIf",
            "Protocols that need the clear-text password (CHAP, Digest) stop working for these accounts.",
            ObjectRevert("DH-KRB-007", "Set-ADAccountControl -Identity $_.DistinguishedName -AllowReversiblePasswordEncryption $true"),
            CheckFilters.ReversibleUsers);

        yield return new("UserPasswordAttribute", "DH-KRB-008", 7,
            "Accounts with the userPassword or unixUserPassword attribute set (DirHealth only checks that a value exists — it never reads it).",
            "These attributes often hold readable or weakly hashed passwords that many accounts can read.",
            "Clear userPassword and unixUserPassword; store service secrets in a vault instead.",
            [MsSecuringAd],
            $"{Ps}\n$targets = Get-ADObject -LDAPFilter \"{CheckFilters.UserPasswordAttribute}\"\n" +
            "$targets | Select-Object DistinguishedName | Export-Csv .\\dirhealth-DH-KRB-008-backup.csv -NoTypeInformation   # names only — the secret values are not backed up\n" +
            "$targets | Set-ADObject -Clear userPassword, unixUserPassword -WhatIf   # remove -WhatIf to apply",
            "Unix/LDAP applications that authenticate against these attributes stop working.",
            "# Not restorable on purpose: the secret values are never exported. Clearing them is the intended end state.",
            CheckFilters.UserPasswordAttribute);

        // ── S19-5 delegation and trusts ──────────────────────────────────────
        yield return new("DelegationToDomainControllers", "DH-KRB-009", 10,
            "Accounts with constrained delegation to a domain controller service or krbtgt, or resource-based delegation configured on a DC or krbtgt.",
            "Whoever controls such an account can impersonate any user — including Domain Admins — against the DC itself.",
            "Remove delegation that targets domain controllers or krbtgt; no regular account needs it.",
            [MsDelegation, Mitre("T1558", "Steal or Forge Kerberos Tickets")],
            $"{Ps}\n$dcs = (Get-ADDomainController -Filter *).HostName + (Get-ADDomainController -Filter *).Name\n" +
            $"$targets = Get-ADObject -LDAPFilter \"{CheckFilters.ConstrainedDelegationSources}\" -Properties msDS-AllowedToDelegateTo |\n" +
            "    Where-Object { $_.'msDS-AllowedToDelegateTo' | Where-Object { $h = ($_ -split '/')[1] -split ':' | Select-Object -First 1; $_ -like 'krbtgt/*' -or $dcs -contains $h -or $dcs -contains ($h -split '\\.')[0] } }\n" +
            "$targets | Select-Object DistinguishedName, @{n='Targets';e={$_.'msDS-AllowedToDelegateTo' -join ';'}} | Export-Csv .\\dirhealth-DH-KRB-009-backup.csv -NoTypeInformation\n" +
            "$targets | Set-ADObject -Clear msDS-AllowedToDelegateTo -WhatIf\n" +
            $"Get-ADObject -LDAPFilter \"{CheckFilters.RbcdOnDcOrKrbtgt}\" | Set-ADObject -Clear msDS-AllowedToActOnBehalfOfOtherIdentity -WhatIf   # remove -WhatIf to apply",
            "Services that rely on this delegation stop forwarding credentials; resource-based delegation on DCs is never needed in normal operation.",
            ObjectRevert("DH-KRB-009", "Set-ADObject -Identity $_.DistinguishedName -Replace @{'msDS-AllowedToDelegateTo' = $_.Targets -split ';'}") +
            "\n# Resource-based delegation on DCs/krbtgt is not restored on purpose.",
            CheckFilters.ConstrainedDelegationSources);

        yield return new("DelegationToMissingAccounts", "DH-KRB-010", 4,
            "Accounts with constrained delegation to a service (SPN) that no account in the domain provides.",
            "An attacker who registers that SPN on an account they control receives the delegated credentials.",
            "Remove delegation entries that point at services that no longer exist.",
            [MsDelegation],
            $"{Ps}\n$targets = Get-ADObject -LDAPFilter \"{CheckFilters.ConstrainedDelegationSources}\" -Properties msDS-AllowedToDelegateTo\n" +
            "$missing = foreach ($t in $targets) { foreach ($spn in $t.'msDS-AllowedToDelegateTo') {\n" +
            "    $host = (($spn -split '/', 2)[1] -split ':')[0]\n" +
            "    if (-not (Get-ADObject -LDAPFilter \"(|(servicePrincipalName=$spn)(servicePrincipalName=HOST/$host))\")) { [pscustomobject]@{ DistinguishedName = $t.DistinguishedName; SPN = $spn } } } }\n" +
            "$missing | Export-Csv .\\dirhealth-DH-KRB-010-backup.csv -NoTypeInformation\n" +
            "$missing | ForEach-Object { Set-ADObject -Identity $_.DistinguishedName -Remove @{'msDS-AllowedToDelegateTo' = $_.SPN} -WhatIf }   # remove -WhatIf to apply",
            "None, if the service really no longer exists; check renamed hosts first.",
            ObjectRevert("DH-KRB-010", "Set-ADObject -Identity $_.DistinguishedName -Add @{'msDS-AllowedToDelegateTo' = $_.SPN}"),
            CheckFilters.ConstrainedDelegationSources);

        yield return new("TrustWithoutSidFiltering", "DH-TRU-001", 8,
            "Outgoing trusts without SID filtering: external trusts without quarantine, or forest trusts with SID history enabled.",
            "An administrator of the other domain can grant themselves any SID of this domain — including Domain Admins — through SID history.",
            "Enable SID filtering (quarantine) on external trusts and disable SID history on forest trusts unless a migration is running.",
            [Mitre("T1134.005", "Access Token Manipulation: SID-History Injection")],
            $"{Ps}\nGet-ADTrust -Filter * | Select-Object Name, Direction, TrustAttributes, SIDFilteringQuarantined, SIDFilteringForestAware | Export-Csv .\\dirhealth-DH-TRU-001-backup.csv -NoTypeInformation\n" +
            "# External trust: netdom trust <this-domain> /domain:<other-domain> /quarantine:yes\n" +
            "# Forest trust:   netdom trust <this-domain> /domain:<other-forest> /enablesidhistory:no\n" +
            "# (netdom has no -WhatIf: run the Get-ADTrust line first and check the backup file)",
            "Migrations that rely on SID history break; users of the other domain lose access granted through their old SIDs.",
            "# Undo: netdom trust <this-domain> /domain:<other> /quarantine:no   (or /enablesidhistory:yes) — see the backup file for the previous state.",
            null);

        yield return new("TrustTgtDelegation", "DH-TRU-002", 7,
            "Outgoing trusts that allow TGT delegation across the trust.",
            "Unconstrained delegation in the other forest can then capture tickets of this forest's users, including admins.",
            "Disable TGT delegation on the trust (default since 2019 updates).",
            [MsDelegation, Mitre("T1558", "Steal or Forge Kerberos Tickets")],
            $"{Ps}\nGet-ADTrust -Filter * | Select-Object Name, TrustAttributes | Export-Csv .\\dirhealth-DH-TRU-002-backup.csv -NoTypeInformation\n" +
            "# netdom trust <this-domain> /domain:<other-forest> /EnableTGTDelegation:No\n" +
            "# (netdom has no -WhatIf: check the backup file first)",
            "Cross-forest scenarios that need unconstrained delegation stop working.",
            "# Undo: netdom trust <this-domain> /domain:<other-forest> /EnableTGTDelegation:Yes",
            null);

        yield return new("TrustInactive", "DH-TRU-003", 2,
            $"Trusts whose trust object has not changed for {Checks.CheckLogic.TrustInactiveDays} days (it normally updates about every 30 days while in use).",
            "Forgotten trusts keep a door open that nobody watches.",
            "Confirm the trust is no longer needed with its owner, then remove it.",
            [MsSecuringAd],
            $"{Ps}\nGet-ADTrust -Filter * | Select-Object Name, Direction, whenChanged   # read-only list — remove with Remove-ADTrust after confirming",
            "None from this list; removing a trust that is still used breaks cross-domain access.",
            "# Read-only listing — nothing to undo.",
            null);

        // ══ Sprint 20 — remaining LDAP checks ════════════════════════════════
        yield return new("LapsNotDeployed", "DH-CMP-004", 7,
            "Neither legacy LAPS nor Windows LAPS is present in the schema.",
            "Without LAPS, local administrator passwords are usually identical across machines — one stolen hash opens every computer.",
            "Deploy Windows LAPS (built into current Windows) and enable it by GPO for all workstations and member servers.",
            [new("Microsoft: Windows LAPS overview", "https://learn.microsoft.com/en-us/windows-server/identity/laps/laps-overview"), Mitre("T1078.003", "Valid Accounts: Local Accounts")],
            $"{Ps}\nGet-ADObject -SearchBase (Get-ADRootDSE).schemaNamingContext -LDAPFilter '(|(lDAPDisplayName=ms-Mcs-AdmPwdExpirationTime)(lDAPDisplayName=msLAPS-PasswordExpirationTime))'   # read-only check\n" +
            "# Deploying LAPS changes the schema and GPOs — follow Microsoft's guide (Update-LapsADSchema), not a one-liner.",
            "None from this list; the schema update itself is forest-wide and irreversible.",
            "# Read-only listing — nothing to undo.",
            null);

        yield return new("ComputersWithoutLaps", "DH-CMP-005", 5,
            "Enabled computers (no DCs) without a LAPS password expiry attribute — LAPS does not manage their local admin password. DirHealth never reads the password itself.",
            "These machines likely share or never change their local administrator password.",
            "Link the LAPS GPO to the OUs of these computers and verify the expiry attribute gets set.",
            [new("Microsoft: Windows LAPS overview", "https://learn.microsoft.com/en-us/windows-server/identity/laps/laps-overview")],
            $"{Ps}\nGet-ADComputer -LDAPFilter \"{CheckFilters.ComputersWithoutLaps(Checks.CheckLogic.LapsExpiryAttributes)}\" | Select-Object Name, DistinguishedName   # read-only list — fix by GPO\n" +
            "# Remove the attribute names from the filter that your schema does not have (DirHealth does this automatically).",
            "None from this list.",
            "# Read-only listing — nothing to undo.",
            null);

        yield return new("MachinePasswordNotRotated", "DH-CMP-006", 4,
            $"Active computers whose machine password is older than {CheckFilters.MachinePasswordMaxDays} days (domain controllers: {CheckFilters.DcPasswordMaxDays} days) — normally it rotates every 30 days.",
            "Rotation was disabled (DisablePasswordChange) or broken; a stolen machine hash stays usable indefinitely.",
            "Re-enable machine password rotation (GPO \"Domain member: Disable machine account password changes\" = Disabled) and reset the machine password.",
            [MsSecuringAd],
            $"{Ps}\n$pwd = (Get-Date).AddDays(-{CheckFilters.MachinePasswordMaxDays}).ToFileTimeUtc(); $logon = $pwd\n" +
            "Get-ADComputer -LDAPFilter \"" + CheckFilters.StaleMachinePasswords("$pwd", "$logon") + "\" -Properties PasswordLastSet |\n" +
            "    Select-Object Name, PasswordLastSet   # read-only list — on each machine: Test-ComputerSecureChannel -Repair",
            "None from this list; resetting the secure channel briefly interrupts domain authentication of that machine.",
            "# Read-only listing — nothing to undo.",
            null);

        yield return new("DuplicateObjects", "DH-GRP-003", 2,
            "Replication-conflict objects (\"CNF:\" in the name) or duplicate sAMAccountNames ($DUPLICATE-).",
            "Duplicates confuse permissions and logons; a conflict object may silently hold rights the original lost.",
            "Compare each conflict object with its original, move needed attributes over, then delete the conflict object.",
            [MsSecuringAd],
            $"{Ps}\nGet-ADObject -LDAPFilter \"{CheckFilters.ConflictObjects}\" -Properties whenCreated | Select-Object Name, DistinguishedName, whenCreated   # read-only list",
            "None from this list; deleting the wrong copy loses its group memberships.",
            "# Read-only listing — nothing to undo.",
            CheckFilters.ConflictObjects);

        yield return new("AnonymousLdapAllowed", "DH-DOM-008", 7,
            "dSHeuristics allows anonymous LDAP operations (7th character = 2).",
            "Anyone on the network can read directory data without an account.",
            "Reset the 7th character of dSHeuristics to 0 (or clear dSHeuristics if nothing else is set).",
            [MsSecuringAd],
            $"{Ps}\n$ds = \"CN=Directory Service,CN=Windows NT,CN=Services,$((Get-ADRootDSE).configurationNamingContext)\"\n" +
            "Get-ADObject $ds -Properties dSHeuristics | Select-Object dSHeuristics | Export-Csv .\\dirhealth-DH-DOM-008-backup.csv -NoTypeInformation\n" +
            "$h = (Get-ADObject $ds -Properties dSHeuristics).dSHeuristics; $new = $h.Substring(0,6) + '0' + $h.Substring(7)\n" +
            "Set-ADObject $ds -Replace @{dSHeuristics = $new} -WhatIf   # remove -WhatIf to apply",
            "Applications that read AD anonymously (old printers, Unix tools) stop working.",
            "$ds = \"CN=Directory Service,CN=Windows NT,CN=Services,$((Get-ADRootDSE).configurationNamingContext)\"\n" +
            "Import-Csv .\\dirhealth-DH-DOM-008-backup.csv | ForEach-Object { Set-ADObject $ds -Replace @{dSHeuristics = $_.dSHeuristics} }",
            null);

        yield return new("AdminSdExMask", "DH-DOM-009", 6,
            "dSHeuristics excludes operator groups from AdminSDHolder protection (AdminSDExMask, 16th character).",
            "Members of the excluded operator groups lose the protected ACL — delegated admins can take them over.",
            "Set the 16th character of dSHeuristics back to 0 so SDProp protects all operator groups again.",
            [MsProtectedAccounts],
            $"{Ps}\n$ds = \"CN=Directory Service,CN=Windows NT,CN=Services,$((Get-ADRootDSE).configurationNamingContext)\"\n" +
            "Get-ADObject $ds -Properties dSHeuristics | Select-Object dSHeuristics | Export-Csv .\\dirhealth-DH-DOM-009-backup.csv -NoTypeInformation\n" +
            "$h = (Get-ADObject $ds -Properties dSHeuristics).dSHeuristics; $new = $h.Substring(0,15) + '0' + $h.Substring(16)\n" +
            "Set-ADObject $ds -Replace @{dSHeuristics = $new} -WhatIf   # remove -WhatIf to apply",
            "Delegations that relied on editing operator-group members stop working after SDProp runs.",
            "$ds = \"CN=Directory Service,CN=Windows NT,CN=Services,$((Get-ADRootDSE).configurationNamingContext)\"\n" +
            "Import-Csv .\\dirhealth-DH-DOM-009-backup.csv | ForEach-Object { Set-ADObject $ds -Replace @{dSHeuristics = $_.dSHeuristics} }",
            null);

        yield return new("SmartcardOldPassword", "DH-USR-006", 5,
            $"Enabled smart-card-required accounts whose password hash is older than {CheckFilters.SmartcardPasswordMaxDays} days.",
            "With smart-card logon the NTLM hash never changes by itself — a hash stolen years ago still works for pass-the-hash.",
            "Toggle \"Smart card is required\" off and on (rotates the hash) or enable automatic rolling of NTLM secrets (domain level 2016).",
            [Mitre("T1550.002", "Use Alternate Authentication Material: Pass the Hash")],
            ObjectFix("DH-USR-006", "Get-ADUser", CheckFilters.SmartcardOldPassword("$cutoff"), "Set-ADUser -SmartcardLogonRequired $false",
                Cutoff(CheckFilters.SmartcardPasswordMaxDays)) +
            "\n$targets | Set-ADUser -SmartcardLogonRequired $true -WhatIf   # second step: re-enable → new random hash",
            "Between the two steps the account can log on with a password; run both steps together.",
            ObjectRevert("DH-USR-006", "Set-ADUser -Identity $_.DistinguishedName -SmartcardLogonRequired $true"),
            CheckFilters.SmartcardOldPassword("$cutoff"));
    }
}
