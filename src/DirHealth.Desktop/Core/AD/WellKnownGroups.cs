using System.Text;
using System.Text.RegularExpressions;

namespace DirHealth.Desktop.Core.AD;

/// <summary>Where a well-known group's SID comes from.</summary>
public enum GroupSidSource
{
    /// <summary>Domain SID + fixed RID (e.g. Domain Admins = 512).</summary>
    Domain,
    /// <summary>Forest-root domain SID + fixed RID (Enterprise Admins, Schema Admins, Enterprise Key Admins).</summary>
    ForestRoot,
    /// <summary>Fixed well-known SID under BUILTIN (S-1-5-32-…).</summary>
    Builtin,
    /// <summary>No fixed RID (DnsAdmins) — must be found by name.</summary>
    NameOnly,
}

public record WellKnownGroupDef(string FallbackName, string Risk, GroupSidSource Source, int Rid);

/// <summary>
/// Resolves admin groups by SID instead of name (S13-1). Group names are localized —
/// on a German-installed domain "Domain Admins" is "Domänen-Admins" — but RIDs and
/// builtin SIDs are identical on every installation. Pure functions, no LDAP.
/// </summary>
public static class WellKnownGroups
{
    public const int DomainAdminsRid          = 512;
    public const int DomainUsersRid           = 513;
    public const int DomainComputersRid       = 515;
    public const int DomainControllersRid     = 516;
    public const int SchemaAdminsRid          = 518;
    public const int EnterpriseAdminsRid      = 519;
    public const int KeyAdminsRid             = 526;
    public const int EnterpriseKeyAdminsRid   = 527;

    public const int BuiltinAdministratorsRid = 544;
    public const int AccountOperatorsRid      = 548;
    public const int ServerOperatorsRid       = 549;
    public const int PrintOperatorsRid        = 550;
    public const int BackupOperatorsRid       = 551;
    public const int RemoteDesktopUsersRid    = 555;

    public const string BuiltinDomainSid = "S-1-5-32";

    public static readonly WellKnownGroupDef DomainAdmins =
        new("Domain Admins", "Full domain control", GroupSidSource.Domain, DomainAdminsRid);

    /// <summary>Groups shown in the Privileged Groups view. Order = display order.</summary>
    public static readonly IReadOnlyList<WellKnownGroupDef> Privileged =
    [
        DomainAdmins,
        new("Enterprise Admins",     "Full forest control",                          GroupSidSource.ForestRoot, EnterpriseAdminsRid),
        new("Schema Admins",         "Can permanently alter AD schema",              GroupSidSource.ForestRoot, SchemaAdminsRid),
        new("Administrators",        "Full control of all domain controllers",       GroupSidSource.Builtin,    BuiltinAdministratorsRid),
        new("Key Admins",            "Can write msDS-KeyCredentialLink (shadow credentials) on domain objects", GroupSidSource.Domain, KeyAdminsRid),
        new("Enterprise Key Admins", "Can write msDS-KeyCredentialLink forest-wide", GroupSidSource.ForestRoot, EnterpriseKeyAdminsRid),
        new("Backup Operators",      "Can read all files including NTDS.dit",        GroupSidSource.Builtin,    BackupOperatorsRid),
        new("Account Operators",     "Can manage accounts and groups",               GroupSidSource.Builtin,    AccountOperatorsRid),
        new("Server Operators",      "Can log on to and manage DCs",                 GroupSidSource.Builtin,    ServerOperatorsRid),
        new("Print Operators",       "Can load driver code on DCs",                  GroupSidSource.Builtin,    PrintOperatorsRid),
        new("DnsAdmins",             "Can execute DLL code on DCs via DNS plugin",   GroupSidSource.NameOnly,   0),
        new("Remote Desktop Users",  "Can RDP into all DCs",                         GroupSidSource.Builtin,    RemoteDesktopUsersRid),
    ];

    // Tier 0 by SID (S15-4): domain-relative RIDs in any domain, plus BUILTIN operators.
    // DnsAdmins (no fixed RID) and Cert Publishers follow in S19-1.
    private static readonly HashSet<int> Tier0DomainRids =
        [DomainAdminsRid, SchemaAdminsRid, EnterpriseAdminsRid, KeyAdminsRid, EnterpriseKeyAdminsRid];

    private static readonly HashSet<string> Tier0BuiltinSids =
    [
        BuiltinGroupSid(BuiltinAdministratorsRid), BuiltinGroupSid(AccountOperatorsRid),
        BuiltinGroupSid(ServerOperatorsRid),       BuiltinGroupSid(PrintOperatorsRid),
        BuiltinGroupSid(BackupOperatorsRid),
    ];

    /// <summary>Is this group SID a Tier 0 (control-the-domain) group? Membership, not exposure.</summary>
    public static bool IsTier0Sid(string? sid)
    {
        if (string.IsNullOrEmpty(sid)) return false;
        if (Tier0BuiltinSids.Contains(sid)) return true;
        return AccountSidPattern.IsMatch(sid)
            && int.TryParse(sid[(sid.LastIndexOf('-') + 1)..], out var rid)
            && Tier0DomainRids.Contains(rid);
    }

    private static readonly Regex DomainSidPattern =
        new(@"^S-1-5-21-\d+-\d+-\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex AccountSidPattern =
        new(@"^S-1-5-21-\d+-\d+-\d+-\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>True for a domain SID (S-1-5-21-a-b-c, no RID).</summary>
    public static bool IsDomainSid(string? sid) => sid is not null && DomainSidPattern.IsMatch(sid);

    /// <summary>Domain SID + RID → group SID. Throws on a malformed domain SID so a bad
    /// value can never reach an LDAP filter.</summary>
    public static string DomainGroupSid(string domainSid, int rid)
    {
        if (!IsDomainSid(domainSid))
            throw new ArgumentException($"Not a domain SID: '{domainSid}'", nameof(domainSid));
        if (rid <= 0)
            throw new ArgumentOutOfRangeException(nameof(rid));
        return $"{domainSid}-{rid}";
    }

    public static string BuiltinGroupSid(int rid) => $"{BuiltinDomainSid}-{rid}";

    /// <summary>SID for a definition. domainSid/forestRootSid may be null when unknown;
    /// returns null for NameOnly groups or when the needed SID is missing.</summary>
    public static string? SidFor(WellKnownGroupDef def, string? domainSid, string? forestRootSid) =>
        def.Source switch
        {
            GroupSidSource.Builtin    => BuiltinGroupSid(def.Rid),
            GroupSidSource.Domain     => IsDomainSid(domainSid) ? DomainGroupSid(domainSid!, def.Rid) : null,
            GroupSidSource.ForestRoot => IsDomainSid(forestRootSid) ? DomainGroupSid(forestRootSid!, def.Rid) : null,
            _                         => null,
        };

    /// <summary>User/computer SID → its domain SID (strip the trailing RID).</summary>
    public static string DomainSidOf(string accountSid)
    {
        if (accountSid is null || !AccountSidPattern.IsMatch(accountSid))
            throw new ArgumentException($"Not a domain account SID: '{accountSid}'", nameof(accountSid));
        return accountSid[..accountSid.LastIndexOf('-')];
    }

    /// <summary>primaryGroupID is a RID relative to the account's own domain (S13-2).</summary>
    public static string PrimaryGroupSid(string accountSid, int primaryGroupId) =>
        DomainGroupSid(DomainSidOf(accountSid), primaryGroupId);

    /// <summary>
    /// Binary objectSid → "S-1-…" string. Layout (MS-DTYP 2.4.2.2): revision (1 byte),
    /// sub-authority count (1 byte), identifier authority (6 bytes, big-endian),
    /// sub-authorities (4 bytes each, little-endian). Implemented by hand so it is
    /// testable without Windows (SecurityIdentifier is Windows-only).
    /// </summary>
    public static string SidBytesToString(byte[] sid)
    {
        ArgumentNullException.ThrowIfNull(sid);
        if (sid.Length < 8)
            throw new ArgumentException("SID too short", nameof(sid));
        int count = sid[1];
        if (sid.Length != 8 + 4 * count)
            throw new ArgumentException("SID length does not match sub-authority count", nameof(sid));

        long authority = 0;
        for (int i = 2; i < 8; i++)
            authority = (authority << 8) | sid[i];

        var sb = new StringBuilder($"S-{sid[0]}-{authority}");
        for (int i = 0; i < count; i++)
            sb.Append('-').Append(BitConverter.ToUInt32(sid, 8 + 4 * i));
        return sb.ToString();
    }

    /// <summary>
    /// LDAP filter that is true for members of the group with this DN, nested
    /// membership included (LDAP_MATCHING_RULE_IN_CHAIN), plus accounts that hold the
    /// group only as primary group — primary-group membership is not stored in
    /// memberOf, so the chain rule alone misses it.
    /// </summary>
    public static string MembershipClause(string escapedGroupDn, int? primaryGroupRid) =>
        primaryGroupRid is int rid
            ? $"(|(memberOf:1.2.840.113556.1.4.1941:={escapedGroupDn})(primaryGroupID={rid}))"
            : $"(memberOf:1.2.840.113556.1.4.1941:={escapedGroupDn})";
}
