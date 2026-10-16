using System.DirectoryServices;
using DirHealth.Desktop.Core.Export;

namespace DirHealth.Desktop.Core.AD;

public class AdConnector
{
    public string? Domain     { get; set; }
    public string? Username   { get; set; }
    public string? Password   { get; set; }

    public DirectoryEntry GetRootEntry()
    {
        if (!string.IsNullOrEmpty(Domain))
        {
            var path = $"LDAP://{Domain}";
            return string.IsNullOrEmpty(Username)
                ? new DirectoryEntry(path)
                : new DirectoryEntry(path, Username, Password);
        }

        using var rootDse = new DirectoryEntry("LDAP://RootDSE");
        var nc = rootDse.Properties["defaultNamingContext"].Value?.ToString()
            ?? throw new InvalidOperationException("Cannot resolve AD domain root (defaultNamingContext empty).");
        return string.IsNullOrEmpty(Username)
            ? new DirectoryEntry($"LDAP://{nc}")
            : new DirectoryEntry($"LDAP://{nc}", Username, Password);
    }

    public DirectorySearcher CreateSearcher(DirectoryEntry root, string filter, params string[] properties)
    {
        var searcher = new DirectorySearcher(root)
        {
            Filter    = filter,
            PageSize  = 1000,
            SizeLimit = 0
        };
        if (properties.Length > 0)
            searcher.PropertiesToLoad.AddRange(properties);
        return searcher;
    }

    public DirectoryEntry GetEntry(string ldapPath) =>
        string.IsNullOrEmpty(Username)
            ? new DirectoryEntry(ldapPath)
            : new DirectoryEntry(ldapPath, Username, Password);

    public bool TestConnection()
    {
        try
        {
            using var entry = GetRootEntry();
            _ = entry.NativeObject;
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Is the signed-in account a (nested or primary-group) member of Domain Admins?
    /// The group is found by SID (domain SID + RID 512), so this works on localized
    /// domains where the group is called e.g. "Domänen-Admins" (S13-1).
    /// Distinguishes "not an admin" from "could not check" — a failed query must not
    /// read as a negative answer.
    /// </summary>
    public AdminCheckResult CheckDomainAdmin()
    {
        string samName;
        if (!string.IsNullOrEmpty(Username))
        {
            samName = Username.Contains('\\') ? Username.Split('\\')[1] : Username;
        }
        else
        {
            var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            samName = identity.Name.Contains('\\') ? identity.Name.Split('\\')[1] : identity.Name;
        }

        try
        {
            using var root = GetRootEntry();
            var group = ResolveGroup(root, WellKnownGroups.DomainAdmins);
            if (group.DistinguishedName is null)
                return AdminCheckResult.Failed(group.Error ?? "Domain Admins group not found.", group.Name);

            using var memberSearcher = CreateSearcher(root,
                $"(&(objectClass=user)(sAMAccountName={EscapeFilterValue(samName)})" +
                WellKnownGroups.MembershipClause(EscapeDn(group.DistinguishedName), WellKnownGroups.DomainAdminsRid) + ")",
                "sAMAccountName");
            return memberSearcher.FindOne() != null
                ? AdminCheckResult.Admin(group.Name)
                : AdminCheckResult.NotAdmin(group.Name);
        }
        catch (Exception ex)
        {
            return AdminCheckResult.Failed(ex.Message, WellKnownGroups.DomainAdmins.FallbackName);
        }
    }

    /// <summary>
    /// Who/where for export metadata (S14-5): domain, the DC that actually answers this
    /// connection, and the account used. Read-only bind; failures come back as "unknown".
    /// </summary>
    public ExportContext GetExportContext()
    {
        string dc;
        try
        {
            using var root = GetRootEntry();
            dc = root.Options?.GetCurrentServerName() ?? "unknown";
        }
        catch { dc = "unknown"; }
        var user = !string.IsNullOrEmpty(Username)
            ? Username
            : System.Security.Principal.WindowsIdentity.GetCurrent().Name;
        return new ExportContext(Domain ?? Environment.UserDomainName, dc, user);
    }

    // ── SID-based group resolution (S13-1) ────────────────────────────────────

    /// <summary>objectSid of the domain root entry as "S-1-5-21-…".</summary>
    public static string? GetDomainSid(DirectoryEntry domainRoot) =>
        domainRoot.Properties["objectSid"].Value is byte[] bytes
            ? WellKnownGroups.SidBytesToString(bytes)
            : null;

    private DirectoryEntry GetRootDse() =>
        GetEntry(string.IsNullOrEmpty(Domain) ? "LDAP://RootDSE" : $"LDAP://{Domain}/RootDSE");

    /// <summary>
    /// Entry for the forest-root domain (home of Enterprise/Schema Admins). In a
    /// single-domain forest that is the current domain; in a child domain we bind to
    /// the root domain's DNS name derived from its DN. Caller disposes.
    /// </summary>
    private DirectoryEntry GetForestRootEntry(DirectoryEntry domainRoot)
    {
        using var dse  = GetRootDse();
        var rootNc     = dse.Properties["rootDomainNamingContext"].Value?.ToString();
        var defaultNc  = dse.Properties["defaultNamingContext"].Value?.ToString();
        if (string.IsNullOrEmpty(rootNc) || string.Equals(rootNc, defaultNc, StringComparison.OrdinalIgnoreCase))
            return GetEntry(domainRoot.Path);
        return GetEntry($"LDAP://{DnToDnsName(rootNc)}/{rootNc}");
    }

    /// <summary>"DC=corp,DC=example,DC=com" → "corp.example.com".</summary>
    public static string DnToDnsName(string dn) =>
        string.Join('.', dn.Split(',')
            .Select(p => p.Trim())
            .Where(p => p.StartsWith("DC=", StringComparison.OrdinalIgnoreCase))
            .Select(p => p[3..]));

    /// <summary>
    /// Finds a well-known group by SID (or by name for NameOnly groups such as
    /// DnsAdmins). Never throws: lookup problems come back in <see cref="ResolvedGroup.Error"/>
    /// so a caller can show "not found" instead of silently dropping the group.
    /// </summary>
    public ResolvedGroup ResolveGroup(DirectoryEntry domainRoot, WellKnownGroupDef def)
    {
        try
        {
            if (def.Source == GroupSidSource.NameOnly)
                return FindGroup(domainRoot, $"(&(objectClass=group)(cn={EscapeFilterValue(def.FallbackName)}))", def);

            if (def.Source == GroupSidSource.ForestRoot)
            {
                using var forestRoot = GetForestRootEntry(domainRoot);
                var forestSid = GetDomainSid(forestRoot);
                var sid = WellKnownGroups.SidFor(def, null, forestSid);
                return sid is null
                    ? new ResolvedGroup(def, null, def.FallbackName, "Forest root SID could not be read.")
                    : FindGroup(forestRoot, $"(&(objectClass=group)(objectSid={sid}))", def);
            }

            var domainSid = GetDomainSid(domainRoot);
            var groupSid  = WellKnownGroups.SidFor(def, domainSid, null);
            return groupSid is null
                ? new ResolvedGroup(def, null, def.FallbackName, "Domain SID could not be read.")
                : FindGroup(domainRoot, $"(&(objectClass=group)(objectSid={groupSid}))", def);
        }
        catch (Exception ex)
        {
            return new ResolvedGroup(def, null, def.FallbackName, ex.Message);
        }
    }

    private ResolvedGroup FindGroup(DirectoryEntry searchRoot, string filter, WellKnownGroupDef def)
    {
        using var searcher = CreateSearcher(searchRoot, filter, "distinguishedName", "cn");
        var result = searcher.FindOne();
        if (result is null)
            return new ResolvedGroup(def, null, def.FallbackName, "Group not found.");
        var dn = result.Properties["distinguishedName"].Count > 0 ? result.Properties["distinguishedName"][0]?.ToString() : null;
        var cn = result.Properties["cn"].Count > 0 ? result.Properties["cn"][0]?.ToString() : null;
        return new ResolvedGroup(def, dn, string.IsNullOrEmpty(cn) ? def.FallbackName : cn, null);
    }

    public static string EscapeDn(string dn) =>
        dn.Replace("\\", "\\5c").Replace("(", "\\28").Replace(")", "\\29");

    public static string EscapeFilterValue(string value) =>
        value.Replace("\\", "\\5c").Replace("*",  "\\2a")
             .Replace("(",  "\\28").Replace(")",  "\\29")
             .Replace("\0", "\\00");
}

/// <summary>Result of the login admin check (S13-1).</summary>
public enum AdminCheckStatus { Admin, NotAdmin, Failed }

public record AdminCheckResult(AdminCheckStatus Status, string GroupName, string? Error)
{
    public static AdminCheckResult Admin(string group)                 => new(AdminCheckStatus.Admin, group, null);
    public static AdminCheckResult NotAdmin(string group)              => new(AdminCheckStatus.NotAdmin, group, null);
    public static AdminCheckResult Failed(string error, string group)  => new(AdminCheckStatus.Failed, group, error);
    public bool IsAdmin => Status == AdminCheckStatus.Admin;
}

/// <summary>A well-known group looked up in AD. <see cref="Name"/> is the real cn
/// (localized), or the English fallback when not found.</summary>
public record ResolvedGroup(WellKnownGroupDef Def, string? DistinguishedName, string Name, string? Error)
{
    public bool Found => DistinguishedName is not null;
}
