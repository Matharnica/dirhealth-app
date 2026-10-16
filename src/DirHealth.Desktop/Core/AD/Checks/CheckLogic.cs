using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD.Checks;

/// <summary>
/// Pure evaluation logic of the Sprint 19 checks — everything that decides "finding or not" once the
/// LDAP data is read. No LDAP here, so each rule is unit-testable.
/// </summary>
public static class CheckLogic
{
    // ── S19-1 hidden primary group (PingCastle S-PrimaryGroup) ────────────────
    /// <summary>Default primary groups: Domain Users 513, Domain Guests 514 (Guest), Domain Computers 515,
    /// Domain Controllers 516, Read-only DCs 521. Anything else hides a membership from member/memberOf.</summary>
    public static readonly IReadOnlySet<int> StandardPrimaryGroups = new HashSet<int> { 513, 514, 515, 516, 521 };

    public static bool HasHiddenPrimaryGroup(int primaryGroupId) =>
        primaryGroupId > 0 && !StandardPrimaryGroups.Contains(primaryGroupId);

    // ── S19-2 admin hardening (admin = enabled Tier 0 member) ─────────────────
    public const int AdminPasswordMaxDays = 365;

    public record AdminFacts(AdUser User, bool InProtectedUsers, bool HasMailbox);

    [Flags]
    public enum AdminIssue
    {
        None            = 0,
        NotProtected    = 1,   // neither "sensitive and cannot be delegated" nor Protected Users
        Kerberoastable  = 2,
        NoPreauth       = 4,
        OldPassword     = 8,   // > 365 days or never expires
        Mailbox         = 16,
    }

    public static AdminIssue EvaluateAdmin(AdminFacts a, DateTime nowUtc)
    {
        var u = a.User;
        var issues = AdminIssue.None;
        if (!UacFlags.Has(u.UserAccountControl, UacFlags.NotDelegated) && !a.InProtectedUsers) issues |= AdminIssue.NotProtected;
        if (u.HasSpn && !string.Equals(u.SamAccountName, "krbtgt", StringComparison.OrdinalIgnoreCase)) issues |= AdminIssue.Kerberoastable;
        if (UacFlags.AsRepRoastable(u.UserAccountControl)) issues |= AdminIssue.NoPreauth;
        if (u.PasswordNeverExpires || u.PasswordLastSet is null || (nowUtc - u.PasswordLastSet.Value).TotalDays > AdminPasswordMaxDays)
            issues |= AdminIssue.OldPassword;
        if (a.HasMailbox) issues |= AdminIssue.Mailbox;
        return issues;
    }

    // ── S19-3 domain basics ──────────────────────────────────────────────────
    public const int KrbtgtMaxDays = 180;

    public static bool KrbtgtPasswordTooOld(DateTime? pwdLastSetUtc, DateTime nowUtc) =>
        pwdLastSetUtc is null || (nowUtc - pwdLastSetUtc.Value).TotalDays > KrbtgtMaxDays;

    /// <summary>ms-DS-MachineAccountQuota above 0 lets any authenticated user join computers.</summary>
    public static bool MachineAccountQuotaRisky(int quota) => quota > 0;

    /// <summary>msDS-EnabledFeatureBL lists the DCs where the feature is enabled; none = disabled.</summary>
    public static bool RecycleBinDisabled(int enabledFeatureBacklinks) => enabledFeatureBacklinks == 0;

    /// <summary>
    /// Fewer than two DCs is a finding. Zero is not a finding but a failed read — every domain
    /// has at least the DC that answered — so it throws and the check reads "Failed" (S16-4).
    /// </summary>
    public static bool TooFewDomainControllers(int count) =>
        count > 0 ? count < 2 : throw new InvalidOperationException("No domain controller found — the DC query returned nothing.");

    /// <summary>msDS-Behavior-Version of the domain: 7 = Windows Server 2016 (also the level of 2019/2022).</summary>
    public const int MinFunctionalLevel = 7;

    public static string FunctionalLevelName(int level) => level switch
    {
        0 => "Windows 2000", 1 => "Windows Server 2003 interim", 2 => "Windows Server 2003",
        3 => "Windows Server 2008", 4 => "Windows Server 2008 R2", 5 => "Windows Server 2012",
        6 => "Windows Server 2012 R2", 7 => "Windows Server 2016", 10 => "Windows Server 2025",
        _ => $"level {level}",
    };

    /// <summary>Well-known principals that make "Pre-Windows 2000 Compatible Access" readable for everyone.</summary>
    public static readonly IReadOnlyDictionary<string, string> RiskyPreWin2000Members = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["S-1-5-11"] = "Authenticated Users",
        ["S-1-5-7"]  = "Anonymous Logon",
        ["S-1-1-0"]  = "Everyone",
    };

    /// <summary>Member DNs of foreign security principals look like "CN=S-1-5-11,CN=ForeignSecurityPrincipals,DC=…".</summary>
    public static string? RiskyPreWin2000Member(string memberDn)
    {
        var first = memberDn.Split(',')[0];
        var sid = first.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ? first[3..] : first;
        return RiskyPreWin2000Members.TryGetValue(sid, out var name) ? name : null;
    }

    // ── S19-4 Kerberos encryption ─────────────────────────────────────────────
    public const int EncAes128 = 0x08, EncAes256 = 0x10;

    /// <summary>Service account without AES: attribute missing (defaults to RC4) or no AES bit set.</summary>
    public static bool LacksAes(int? supportedEncryptionTypes) =>
        supportedEncryptionTypes is not { } v || (v & (EncAes128 | EncAes256)) == 0;

    // ── S20-2 dSHeuristics (MS-ADTS 6.1.1.2.4.1.2) ────────────────────────────
    /// <summary>7th character "2": anonymous LDAP operations allowed (fLDAPBlockAnonOps off).</summary>
    public static bool AnonymousLdapAllowed(string? dsHeuristics) =>
        dsHeuristics is { Length: >= 7 } h && h[6] == '2';

    /// <summary>16th character (dwAdminSDExMask): non-zero hex digit excludes operator groups from AdminSDHolder protection.</summary>
    public static bool AdminSdExMaskSet(string? dsHeuristics) =>
        dsHeuristics is { Length: >= 16 } h && h[15] != '0' && Uri.IsHexDigit(h[15]);

    /// <summary>Which groups the AdminSDExMask removes from SDProp (bit 1 Account, 2 Server, 4 Print, 8 Backup Operators).</summary>
    public static IReadOnlyList<string> AdminSdExMaskGroups(string? dsHeuristics)
    {
        if (!AdminSdExMaskSet(dsHeuristics)) return [];
        int mask = Convert.ToInt32(dsHeuristics![15].ToString(), 16);
        var list = new List<string>();
        if ((mask & 1) != 0) list.Add("Account Operators");
        if ((mask & 2) != 0) list.Add("Server Operators");
        if ((mask & 4) != 0) list.Add("Print Operators");
        if ((mask & 8) != 0) list.Add("Backup Operators");
        return list;
    }

    /// <summary>LAPS expiry attributes DirHealth knows (legacy and Windows LAPS); the password attributes are never read.</summary>
    public static readonly IReadOnlyList<string> LapsExpiryAttributes = ["ms-Mcs-AdmPwdExpirationTime", "msLAPS-PasswordExpirationTime"];

    // ── S19-5 delegation and trusts ───────────────────────────────────────────
    /// <summary>"cifs/dc01.corp.com:445" → "dc01.corp.com" (lower case).</summary>
    public static string SpnHost(string spn)
    {
        var afterSlash = spn.Contains('/') ? spn[(spn.IndexOf('/') + 1)..] : spn;
        var host = afterSlash.Split('/', ':')[0];
        return host.ToLowerInvariant();
    }

    public static string SpnService(string spn) => spn.Contains('/') ? spn[..spn.IndexOf('/')].ToLowerInvariant() : "";

    /// <summary>Does a delegation target point at a domain controller or krbtgt?</summary>
    public static bool TargetsDomainController(string spn, IReadOnlyCollection<string> dcHostNames)
    {
        if (SpnService(spn) == "krbtgt") return true;
        var host = SpnHost(spn);
        var shortHost = host.Split('.')[0];
        return dcHostNames.Any(dc =>
            string.Equals(dc, host, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dc.Split('.')[0], shortHost, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A target SPN is missing when neither it nor the HOST/ SPN of its host exists (HOST covers cifs, http … via sPNMappings).</summary>
    public static bool SpnIsMissing(string spn, Func<string, bool> spnExists)
    {
        if (spnExists(spn)) return false;
        var host = spn.Contains('/') ? spn[(spn.IndexOf('/') + 1)..].Split(':')[0] : spn;
        return !spnExists("HOST/" + host);
    }

    // trustAttributes bits (MS-ADTS 6.1.6.7.9)
    public const int TrustNonTransitive          = 0x1;
    public const int TrustQuarantinedDomain      = 0x4;    // SID filtering on an external trust
    public const int TrustForestTransitive       = 0x8;
    public const int TrustWithinForest           = 0x20;
    public const int TrustTreatAsExternal        = 0x40;   // forest trust: SID history allowed
    public const int TrustEnableTgtDelegation    = 0x800;
    public const int TrustInactiveDays           = 90;

    public record TrustFacts(string Name, int TrustAttributes, int TrustDirection, DateTime? WhenChangedUtc);

    [Flags]
    public enum TrustIssue { None = 0, NoSidFiltering = 1, TgtDelegation = 2, Inactive = 4 }

    /// <summary>
    /// SID filtering is off when an external trust (not within the forest, not a forest trust) lacks
    /// QUARANTINED_DOMAIN, or a forest trust has TREAT_AS_EXTERNAL (SID history enabled). TGT delegation is
    /// enabled by ENABLE_TGT_DELEGATION. The trust object is rewritten about every 30 days while the trust is
    /// used — unchanged for 90 days means inactive. Only outgoing trust (direction 2 or 3) can be abused this way.
    /// </summary>
    public static TrustIssue EvaluateTrust(TrustFacts t, DateTime nowUtc)
    {
        var issues = TrustIssue.None;
        bool withinForest = (t.TrustAttributes & TrustWithinForest) != 0;
        bool forest       = (t.TrustAttributes & TrustForestTransitive) != 0;
        bool trustsOther  = t.TrustDirection is 2 or 3;   // this domain trusts the other side
        if (!withinForest && trustsOther)
        {
            if (!forest && (t.TrustAttributes & TrustQuarantinedDomain) == 0) issues |= TrustIssue.NoSidFiltering;
            if (forest && (t.TrustAttributes & TrustTreatAsExternal) != 0)     issues |= TrustIssue.NoSidFiltering;
            if ((t.TrustAttributes & TrustEnableTgtDelegation) != 0)           issues |= TrustIssue.TgtDelegation;
        }
        if (!withinForest && t.WhenChangedUtc is { } changed && (nowUtc - changed).TotalDays > TrustInactiveDays)
            issues |= TrustIssue.Inactive;
        return issues;
    }
}
