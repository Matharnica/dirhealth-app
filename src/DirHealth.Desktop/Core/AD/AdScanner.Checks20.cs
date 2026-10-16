using System.DirectoryServices;
using System.Globalization;
using DirHealth.Desktop.Core.AD.Checks;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

// Sprint 20 remaining LDAP checks — read-only. LAPS: only expiry attributes and schema presence are
// read, never a LAPS password attribute.
public partial class AdScanner
{
    private string RootDseValue(string attr)
    {
        using var dse = _connector.GetEntry(string.IsNullOrEmpty(_connector.Domain) ? "LDAP://RootDSE" : $"LDAP://{_connector.Domain}/RootDSE");
        return dse.Properties[attr].Value?.ToString()
               ?? throw new InvalidOperationException($"RootDSE attribute {attr} not available.");
    }

    private DirectoryEntry EntryAt(string dn) =>
        _connector.GetEntry(string.IsNullOrEmpty(_connector.Domain) ? $"LDAP://{dn}" : $"LDAP://{_connector.Domain}/{dn}");

    /// <summary>Which LAPS expiry attributes exist in the schema (by lDAPDisplayName).</summary>
    private List<string> LapsAttributesInSchema()
    {
        using var schema = EntryAt(RootDseValue("schemaNamingContext"));
        var found = new List<string>();
        foreach (var attr in CheckLogic.LapsExpiryAttributes)
        {
            using var s = new DirectorySearcher(schema, $"(&(objectClass=attributeSchema)(lDAPDisplayName={EscapeFilterValue(attr)}))", ["cn"], SearchScope.OneLevel);
            if (s.FindOne() is not null) found.Add(attr);
        }
        return found;
    }

    private static string FileTimeDaysAgo(int days) =>
        DateTime.UtcNow.AddDays(-days).ToFileTimeUtc().ToString(CultureInfo.InvariantCulture);

    // One sub-query per source (schema, computers, conflict objects, dSHeuristics, smart-card
    // users): a failed read marks only its own rules "Failed" and keeps the others' findings (S16-4).
    public Task<List<AdFinding>> GetLapsFindingsAsync() => Task.Run(() =>
    {
        var laps = LapsAttributesInSchema();
        if (laps.Count == 0)
            return [NewFinding("LapsNotDeployed", "LAPS Is Not Deployed (No LAPS Schema Attributes)", FindingSeverity.High,
                [new AffectedObject { Name = "LAPS schema extension", Type = AffectedObjectType.DomainSetting, Detail = "neither legacy nor Windows LAPS" }])];
        using var root = _connector.GetRootEntry();
        var without = QueryObjects(root, CheckFilters.ComputersWithoutLaps(laps)).Select(x => x.Obj).ToList();
        return without.Count > 0
            ? [NewFinding("ComputersWithoutLaps", $"{without.Count} Computer(s) Without a LAPS-Managed Password", FindingSeverity.Medium, without)]
            : new List<AdFinding>();
    });

    // Machine / DC passwords that stopped rotating
    public Task<List<AdFinding>> GetMachinePasswordFindingsAsync() => Task.Run(() =>
    {
        using var root = _connector.GetRootEntry();
        var cutoff = FileTimeDaysAgo(CheckFilters.MachinePasswordMaxDays);
        var stale = QueryObjects(root, CheckFilters.StaleMachinePasswords(cutoff, cutoff))
            .Concat(QueryObjects(root, CheckFilters.StaleDcPasswords(FileTimeDaysAgo(CheckFilters.DcPasswordMaxDays))))
            .Select(x => x.Obj).ToList();
        return stale.Count > 0
            ? [NewFinding("MachinePasswordNotRotated", $"{stale.Count} Active Computer(s) Whose Machine Password Stopped Rotating", FindingSeverity.Medium, stale)]
            : new List<AdFinding>();
    });

    // Replication conflicts / duplicates
    public Task<List<AdFinding>> GetDuplicateObjectFindingsAsync() => Task.Run(() =>
    {
        using var root = _connector.GetRootEntry();
        var conflicts = QueryObjects(root, CheckFilters.ConflictObjects).Select(x => { x.Obj.Detail = "replication conflict / duplicate"; return x.Obj; }).ToList();
        return conflicts.Count > 0
            ? [NewFinding("DuplicateObjects", $"{conflicts.Count} Replication-Conflict or Duplicate Object(s)", FindingSeverity.Low, conflicts)]
            : new List<AdFinding>();
    });

    // dSHeuristics — an absent value is the secure default; an unreadable object is a failure
    public Task<List<AdFinding>> GetDsHeuristicsFindingsAsync() => Task.Run(() =>
    {
        var findings = new List<AdFinding>();
        using var ds = EntryAt($"CN=Directory Service,CN=Windows NT,CN=Services,{RootDseValue("configurationNamingContext")}");
        using var s = new DirectorySearcher(ds, "(objectClass=*)", ["dSHeuristics"], SearchScope.Base);
        var r = s.FindOne() ?? throw new InvalidOperationException("Directory Service object could not be read.");
        var heur = r.Properties["dSHeuristics"].Count > 0 ? r.Properties["dSHeuristics"][0]?.ToString() : null;
        if (CheckLogic.AnonymousLdapAllowed(heur))
            findings.Add(NewFinding("AnonymousLdapAllowed", "Anonymous LDAP Operations Are Allowed (dSHeuristics)", FindingSeverity.High,
                [new AffectedObject { Name = $"dSHeuristics = {heur}", Type = AffectedObjectType.DomainSetting }]));
        var excluded = CheckLogic.AdminSdExMaskGroups(heur);
        if (excluded.Count > 0)
            findings.Add(NewFinding("AdminSdExMask", "Operator Groups Excluded From AdminSDHolder Protection", FindingSeverity.High,
                excluded.Select(g => new AffectedObject { Name = g, Type = AffectedObjectType.DomainSetting, Detail = $"dSHeuristics = {heur}" }).ToList()));
        return findings;
    });

    // Smart-card accounts: their password hash never changes unless rotated
    public Task<List<AdFinding>> GetSmartcardFindingsAsync() => Task.Run(() =>
    {
        var smart = QueryUsers(CheckFilters.SmartcardOldPassword(FileTimeDaysAgo(CheckFilters.SmartcardPasswordMaxDays))).Select(AffectedObject.FromUser).ToList();
        return smart.Count > 0
            ? [NewFinding("SmartcardOldPassword", $"{smart.Count} Smart-Card Account(s) With an Unrotated Password Hash", FindingSeverity.Medium, smart)]
            : new List<AdFinding>();
    });
}
