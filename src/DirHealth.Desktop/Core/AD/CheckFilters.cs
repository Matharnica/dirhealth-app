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

    // ── Sprint 19 ────────────────────────────────────────────────────────────
    /// <summary>Enabled users whose primary group is not Domain Users/Guests (hidden membership).</summary>
    public const string HiddenPrimaryGroupUsers =
        "(&" + EnabledUser + "(!(primaryGroupID=513))(!(primaryGroupID=514)))";

    /// <summary>Enabled computers whose primary group is not Domain Computers/Controllers/RODCs.</summary>
    public const string HiddenPrimaryGroupComputers =
        "(&(objectClass=computer)" + NotDisabled + "(!(primaryGroupID=515))(!(primaryGroupID=516))(!(primaryGroupID=521)))";

    public const string DesOnlyUsers     = "(&" + EnabledUser + "(userAccountControl:1.2.840.113556.1.4.803:=2097152))";
    public const string DesOnlyComputers = "(&(objectClass=computer)" + NotDisabled + "(userAccountControl:1.2.840.113556.1.4.803:=2097152))";

    public const string ReversibleUsers     = "(&" + EnabledUser + "(userAccountControl:1.2.840.113556.1.4.803:=128))";
    public const string ReversibleComputers = "(&(objectClass=computer)" + NotDisabled + "(userAccountControl:1.2.840.113556.1.4.803:=128))";

    /// <summary>Presence only — the attribute values are never requested (S19-4).</summary>
    public const string UserPasswordAttribute =
        "(&(|(objectClass=user)(objectClass=computer))(|(userPassword=*)(unixUserPassword=*)))";

    public const string ConstrainedDelegationSources = "(msDS-AllowedToDelegateTo=*)";

    /// <summary>Resource-based constrained delegation set on a DC or on krbtgt (presence only).</summary>
    public const string RbcdOnDcOrKrbtgt =
        "(&(|(userAccountControl:1.2.840.113556.1.4.803:=8192)(sAMAccountName=krbtgt))(msDS-AllowedToActOnBehalfOfOtherIdentity=*))";

    public const string DomainControllers = "(&(objectClass=computer)(userAccountControl:1.2.840.113556.1.4.803:=8192))";

    public const string Trusts = "(objectClass=trustedDomain)";

    // ── Sprint 20 ────────────────────────────────────────────────────────────
    public const int MachinePasswordMaxDays = 45;
    public const int DcPasswordMaxDays      = 90;
    public const int SmartcardPasswordMaxDays = 365;

    /// <summary>
    /// Enabled computers without any LAPS expiry attribute — only attributes that exist in the schema may be
    /// passed (an unknown attribute makes AD return nothing, which would look like "all covered"). The LAPS
    /// password itself is never requested.
    /// </summary>
    public static string ComputersWithoutLaps(IEnumerable<string> lapsExpiryAttributes) =>
        "(&(objectClass=computer)" + NotDisabled + "(!(userAccountControl:1.2.840.113556.1.4.803:=8192))" +
        string.Concat(lapsExpiryAttributes.Select(a => $"(!({a}=*))")) + ")";

    /// <summary>Active (recent logon) non-DC computers whose machine password stopped rotating.</summary>
    public static string StaleMachinePasswords(string pwdCutoff, string logonCutoff) =>
        "(&(objectClass=computer)" + NotDisabled + "(!(userAccountControl:1.2.840.113556.1.4.803:=8192))" +
        $"(pwdLastSet<={pwdCutoff})(lastLogonTimestamp>={logonCutoff}))";

    public static string StaleDcPasswords(string pwdCutoff) =>
        $"(&(objectClass=computer)(userAccountControl:1.2.840.113556.1.4.803:=8192)(pwdLastSet<={pwdCutoff}))";

    /// <summary>Replication-conflict objects ("CNF:" in the name) and duplicate sAMAccountNames.</summary>
    public const string ConflictObjects = "(|(cn=*CNF:*)(sAMAccountName=$DUPLICATE-*))";

    public static string SmartcardOldPassword(string cutoff) =>
        "(&" + EnabledUser + "(userAccountControl:1.2.840.113556.1.4.803:=262144)" + $"(pwdLastSet<={cutoff}))";

    // ── Sprint 23 ────────────────────────────────────────────────────────────
    /// <summary>
    /// (S23-1) Enabled users/computers and all groups with sIDHistory. Groups have no userAccountControl,
    /// so the "not disabled" clause alone would be undefined for them and drop them — hence the OR.
    /// The SID values are read and evaluated client-side (CheckLogic.IsPrivilegedHistorySid).
    /// </summary>
    public const string SidHistoryAnyObject =
        "(&(sIDHistory=*)(|(objectClass=group)" + NotDisabled + "))";

    /// <summary>(S23-2) Presence only — the key material in msDS-KeyCredentialLink is never requested.</summary>
    public const string ShadowCredentialUsers = "(&" + EnabledUser + "(msDS-KeyCredentialLink=*))";

    /// <summary>(S23-2) Presence only — the certificate mappings in altSecurityIdentities are never requested.</summary>
    public const string AltSecurityIdentityUsers = "(&" + EnabledUser + "(altSecurityIdentities=*))";

    /// <summary>(S23-2) TRUSTED_TO_AUTH_FOR_DELEGATION (0x1000000) — constrained delegation with protocol transition.</summary>
    public const string ProtocolTransitionUsers =
        "(&" + EnabledUser + "(userAccountControl:1.2.840.113556.1.4.803:=16777216))";

    /// <summary>
    /// (S23-3) Last system-state backup (dSASignature metadata) may be at most this old.
    /// 7 days = at least one backup per week (confirmed by the product owner 2026-10-18).
    /// </summary>
    public const int BackupMaxDays = 7;

    /// <summary>(S23-3) Global SYSVOL migration state object (relative to the domain DN) and its "Eliminated" value.</summary>
    public const string DfsrGlobalSettingsRdn = "CN=DFSR-GlobalSettings,CN=System";
    public const int    DfsrEliminated        = 48;
}
