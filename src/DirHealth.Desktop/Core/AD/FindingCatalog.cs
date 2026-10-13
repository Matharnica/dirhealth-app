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
    string? CheckFilter);

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

    private static readonly IReadOnlyDictionary<string, FindingRule> Rules = Build().ToDictionary(r => r.Category);

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
    }
}
