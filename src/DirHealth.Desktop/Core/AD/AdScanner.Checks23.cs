using System.DirectoryServices;
using System.Globalization;
using DirHealth.Desktop.Core.AD.Checks;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

// Sprint 23 checks — read-only LDAP searches only. Decision logic in Checks/CheckLogic.cs, filters in
// CheckFilters, texts/fixes/weights in FindingCatalog. Each method is one sub-query of SplitChecks():
// a read that fails throws, so only its own rules read "Failed" (S16-4) — never a silent "Passed".
public partial class AdScanner
{
    // ── S23-1 privileged SID in sIDHistory ────────────────────────────────────
    public Task<List<AdFinding>> GetPrivilegedSidHistoryFindingsAsync() => Task.Run(() =>
    {
        using var root = _connector.GetRootEntry();
        using var s = _connector.CreateSearcher(root, CheckFilters.SidHistoryAnyObject,
            "cn", "sAMAccountName", "distinguishedName", "objectSid", "objectClass", "sIDHistory");
        using var res = s.FindAll();
        var objs = new List<AffectedObject>();
        foreach (SearchResult r in res)
        {
            var p = r.Properties;
            var history = p["sIDHistory"].Cast<object>().OfType<byte[]>().Select(WellKnownGroups.SidBytesToString).ToList();
            var privileged = CheckLogic.PrivilegedHistorySids(history);
            if (privileged.Count == 0) continue;    // unprivileged history stays in DH-USR-005 only
            bool isComputer = false, isGroup = false;
            foreach (var c in p["objectClass"]) { var v = c?.ToString(); isComputer |= v == "computer"; isGroup |= v == "group"; }
            var sam = GetString(p, "sAMAccountName");
            objs.Add(new AffectedObject
            {
                Name              = sam.Length > 0 ? sam : GetString(p, "cn"),
                DistinguishedName = GetString(p, "distinguishedName"),
                Sid               = SidOf(p),
                Type              = isComputer ? AffectedObjectType.Computer : isGroup ? AffectedObjectType.Group : AffectedObjectType.User,
                Detail            = "sIDHistory: " + string.Join(", ", privileged),
            });
        }
        return objs.Count == 0 ? new List<AdFinding>()
            : [NewFinding("PrivilegedSidHistory", $"{objs.Count} Object(s) With a Privileged SID in sIDHistory", FindingSeverity.Critical, objs)];
    });

    // ── S23-2 admin backdoors on Tier 0 accounts ──────────────────────────────
    /// <summary>DNs matching a presence filter — only distinguishedName is loaded, never the attribute value.</summary>
    private HashSet<string> DnsMatching(DirectoryEntry root, string filter)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var s = _connector.CreateSearcher(root, filter, "distinguishedName");
        using var res = s.FindAll();
        foreach (SearchResult r in res) set.Add(GetString(r.Properties, "distinguishedName"));
        return set;
    }

    public Task<List<AdFinding>> GetAdminBackdoorFindingsAsync() => Task.Run(() =>
    {
        using var root = _connector.GetRootEntry();
        var admins  = GetTier0Members(root);            // throws when a Tier 0 group cannot be resolved
        var withKey = DnsMatching(root, CheckFilters.ShadowCredentialUsers);
        var withAlt = DnsMatching(root, CheckFilters.AltSecurityIdentityUsers);

        var shadow = new List<AffectedObject>(); var alt = new List<AffectedObject>(); var s4u = new List<AffectedObject>();
        foreach (var a in admins.Values)
        {
            var issues = CheckLogic.EvaluateBackdoors(new CheckLogic.BackdoorFacts(
                a.UserAccountControl, withKey.Contains(a.DistinguishedName), withAlt.Contains(a.DistinguishedName)));
            if (issues.HasFlag(CheckLogic.BackdoorIssue.ShadowCredentials))     shadow.Add(AffectedObject.FromUser(a));
            if (issues.HasFlag(CheckLogic.BackdoorIssue.AltSecurityIdentities)) alt.Add(AffectedObject.FromUser(a));
            if (issues.HasFlag(CheckLogic.BackdoorIssue.ProtocolTransition))    s4u.Add(AffectedObject.FromUser(a));
        }

        var findings = new List<AdFinding>();
        if (shadow.Count > 0) findings.Add(NewFinding("AdminShadowCredentials", $"{shadow.Count} Tier 0 Account(s) With Key Credentials (Shadow Credentials)", FindingSeverity.High, shadow));
        if (alt.Count > 0)    findings.Add(NewFinding("AdminAltSecurityIdentities", $"{alt.Count} Tier 0 Account(s) With Explicit Certificate Mappings", FindingSeverity.High, alt));
        if (s4u.Count > 0)    findings.Add(NewFinding("AdminProtocolTransition", $"{s4u.Count} Tier 0 Account(s) Trusted for Protocol Transition", FindingSeverity.High, s4u));
        return findings;
    });

    // ── S23-3 recoverability ──────────────────────────────────────────────────
    /// <summary>
    /// Last backup per naming context (domain, configuration, schema) from the dSASignature replication
    /// metadata of the NC head — the same source as "repadmin /showbackup". Missing metadata throws.
    /// </summary>
    public Task<List<AdFinding>> GetBackupFindingsAsync() => Task.Run(() =>
    {
        var now = DateTime.UtcNow;
        var old = new List<AffectedObject>();
        foreach (var ncAttr in new[] { "defaultNamingContext", "configurationNamingContext", "schemaNamingContext" })
        {
            var nc = RootDseValue(ncAttr);
            using var head = EntryAt(nc);
            // Constructed attribute: only returned when requested explicitly with a base search
            using var s = new DirectorySearcher(head, "(objectClass=*)", ["msDS-ReplAttributeMetaData"], SearchScope.Base);
            var r = s.FindOne() ?? throw new InvalidOperationException($"Naming context {nc} could not be read.");
            var meta = r.Properties["msDS-ReplAttributeMetaData"].Cast<object>().OfType<string>().ToList();
            var last = CheckLogic.LastBackupUtc(meta);
            if (CheckLogic.BackupTooOld(last, now))
                old.Add(new AffectedObject
                {
                    Name = nc, DistinguishedName = nc, Type = AffectedObjectType.DomainSetting,
                    Detail = last == DateTime.MinValue ? "never backed up"
                           : string.Format(CultureInfo.InvariantCulture, "last backup {0:yyyy-MM-dd} ({1:0} days ago)", last, (now - last).TotalDays),
                });
        }
        return old.Count > 0
            ? [NewFinding("BackupOutdated", $"No AD Backup in the Last {CheckFilters.BackupMaxDays} Days", FindingSeverity.High, old)]
            : new List<AdFinding>();
    });

    public Task<List<AdFinding>> GetSysvolReplicationFindingsAsync() => Task.Run(() =>
    {
        using var root = _connector.GetRootEntry();
        var domainDn = root.Properties["distinguishedName"].Value?.ToString();
        if (string.IsNullOrEmpty(domainDn)) throw new InvalidOperationException("Domain object could not be read.");
        using var system = EntryAt($"CN=System,{domainDn}");

        using var ds = new DirectorySearcher(system, "(cn=DFSR-GlobalSettings)", ["msDFSR-Flags", "distinguishedName"], SearchScope.OneLevel);
        var dfsr = ds.FindOne();
        int? flags = dfsr is not null && dfsr.Properties["msDFSR-Flags"].Count > 0 ? (int)GetLong(dfsr.Properties, "msDFSR-Flags") : null;

        using var fs = new DirectorySearcher(system, @"(cn=Domain System Volume \28SYSVOL share\29)", ["distinguishedName"], SearchScope.Subtree);
        var frs = fs.FindOne();

        if (!CheckLogic.SysvolOnNtfrs(dfsr is not null, flags, frs is not null)) return new List<AdFinding>();
        var detail = dfsr is null ? "no DFSR-GlobalSettings — migration never started (Start)"
                   : $"msDFSR-Flags = {flags} ({(flags switch { 0 => "Start", 16 => "Prepared", 32 => "Redirected", _ => "unknown" })}), expected {CheckFilters.DfsrEliminated} (Eliminated)";
        return [NewFinding("SysvolNtfrs", "SYSVOL Is Still Replicated by NTFRS", FindingSeverity.High,
            [new AffectedObject { Name = "SYSVOL replication", DistinguishedName = $"{CheckFilters.DfsrGlobalSettingsRdn},{domainDn}",
                                  Type = AffectedObjectType.DomainSetting, Detail = detail }])];
    });
}
