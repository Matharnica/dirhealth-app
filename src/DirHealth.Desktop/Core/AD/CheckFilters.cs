namespace DirHealth.Desktop.Core.AD;

/// <summary>
/// LDAP filters of the scan checks — single source (S16-3). The scanner runs them, and the
/// fix block shows them verbatim inside the PowerShell command (Get-ADUser -LDAPFilter …), so
/// the command targets exactly the objects the check reported (counter-example: PingCastle
/// #267, where the snippet matched 5 computers and the report 18). Time-based filters take
/// the cutoff as text: the scanner passes a FILETIME number, the fix block "$cutoff".
/// </summary>
public static class CheckFilters
{
    // Thresholds of the time-based checks — the fix block computes $cutoff from the same values
    public const int InactiveUserDays     = 90;
    public const int OldPasswordDays      = 365;
    public const int InactiveComputerDays = 90;
    public const int StaleAdminDays       = 30;

    public const string NotDisabled  = "(!(userAccountControl:1.2.840.113556.1.4.803:=2))";
    public const string PersonUser   = "(objectClass=user)(objectCategory=person)";
    public const string EnabledUser  = PersonUser + NotDisabled;

    public static string InactiveUsers(string cutoff) =>
        $"(&{EnabledUser}(|(lastLogonTimestamp<={cutoff})(!(lastLogonTimestamp=*))))";

    public const string PasswordNeverExpires =
        "(&" + EnabledUser + "(userAccountControl:1.2.840.113556.1.4.803:=65536))";

    public static string ExpiredPasswords(string cutoff) =>
        $"(&{EnabledUser}(pwdLastSet<={cutoff})(pwdLastSet>=1))";

    public const string EmptyGroups = "(&(objectClass=group)(!(member=*)))";

    /// <summary>Single-member groups: all groups, member count checked client-side.</summary>
    public const string AllGroups = "(objectClass=group)";

    public static string InactiveComputers(string cutoff) =>
        $"(&(objectClass=computer){NotDisabled}(|(lastLogonTimestamp<={cutoff})(!(lastLogonTimestamp=*))))";

    public const string ComputersWithoutOs = "(&(objectClass=computer)(!(operatingSystem=*)))";

    /// <summary>EOL: all computers with an OS string, matched client-side by EolMatcher.</summary>
    public const string ComputersWithOs = "(&(objectClass=computer)(operatingSystem=*))";

    public const string Kerberoastable =
        "(&" + EnabledUser + "(servicePrincipalName=*)(!(samAccountName=krbtgt)))";

    public const string AdminSdHolder = "(&" + EnabledUser + "(adminCount=1))";

    public const string AsRepRoastable =
        "(&" + EnabledUser + "(userAccountControl:1.2.840.113556.1.4.803:=4194304))";

    /// <summary>TRUSTED_FOR_DELEGATION, domain controllers (SERVER_TRUST_ACCOUNT) excluded.</summary>
    public const string UnconstrainedDelegationComputers =
        "(&(objectClass=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=8192))(userAccountControl:1.2.840.113556.1.4.803:=524288))";

    public const string UnconstrainedDelegationUsers =
        "(&" + EnabledUser + "(userAccountControl:1.2.840.113556.1.4.803:=524288))";

    public const string PasswordNotRequired =
        "(&" + EnabledUser + "(userAccountControl:1.2.840.113556.1.4.803:=32))";

    public const string SidHistory = "(&" + EnabledUser + "(sIDHistory=*))";

    /// <summary>Stale Domain Admins: enabled, nested/primary member of the group, no recent logon.</summary>
    public static string StaleDomainAdmins(string escapedGroupDn, string cutoff) =>
        $"(&{EnabledUser}{WellKnownGroups.MembershipClause(escapedGroupDn, WellKnownGroups.DomainAdminsRid)}" +
        $"(|(lastLogonTimestamp<={cutoff})(!(lastLogonTimestamp=*))))";
}
