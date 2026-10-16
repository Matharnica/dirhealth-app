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

    public async Task<List<AdFinding>> GetRemainingCheckFindingsAsync() => await Task.Run(() =>
    {
        var findings = new List<AdFinding>();
        var now = DateTime.UtcNow;
        string Ft(int days) => now.AddDays(-days).ToFileTimeUtc().ToString(CultureInfo.InvariantCulture);
        using var root = _connector.GetRootEntry();

        // LAPS
        var laps = LapsAttributesInSchema();
        if (laps.Count == 0)
            findings.Add(NewFinding("LapsNotDeployed", "LAPS Is Not Deployed (No LAPS Schema Attributes)", FindingSeverity.High,
                [new AffectedObject { Name = "LAPS schema extension", Type = AffectedObjectType.DomainSetting, Detail = "neither legacy nor Windows LAPS" }]));
        else
        {
            var without = QueryObjects(root, CheckFilters.ComputersWithoutLaps(laps)).Select(x => x.Obj).ToList();
            if (without.Count > 0)
                findings.Add(NewFinding("ComputersWithoutLaps", $"{without.Count} Computer(s) Without a LAPS-Managed Password", FindingSeverity.Medium, without));
        }

        // Machine / DC passwords that stopped rotating
        var stale = QueryObjects(root, CheckFilters.StaleMachinePasswords(Ft(CheckFilters.MachinePasswordMaxDays), Ft(CheckFilters.MachinePasswordMaxDays)))
            .Concat(QueryObjects(root, CheckFilters.StaleDcPasswords(Ft(CheckFilters.DcPasswordMaxDays))))
            .Select(x => x.Obj).ToList();
        if (stale.Count > 0)
            findings.Add(NewFinding("MachinePasswordNotRotated", $"{stale.Count} Active Computer(s) Whose Machine Password Stopped Rotating", FindingSeverity.Medium, stale));

        // Replication conflicts / duplicates
        var conflicts = QueryObjects(root, CheckFilters.ConflictObjects).Select(x => { x.Obj.Detail = "replication conflict / duplicate"; return x.Obj; }).ToList();
        if (conflicts.Count > 0)
            findings.Add(NewFinding("DuplicateObjects", $"{conflicts.Count} Replication-Conflict or Duplicate Object(s)", FindingSeverity.Low, conflicts));

        // dSHeuristics
        var dsPath = $"CN=Directory Service,CN=Windows NT,CN=Services,{RootDseValue("configurationNamingContext")}";
        using (var ds = EntryAt(dsPath))
        {
            using var s = new DirectorySearcher(ds, "(objectClass=*)", ["dSHeuristics"], SearchScope.Base);
            var heur = s.FindOne() is { } r && r.Properties["dSHeuristics"].Count > 0 ? r.Properties["dSHeuristics"][0]?.ToString() : null;
            if (CheckLogic.AnonymousLdapAllowed(heur))
                findings.Add(NewFinding("AnonymousLdapAllowed", "Anonymous LDAP Operations Are Allowed (dSHeuristics)", FindingSeverity.High,
                    [new AffectedObject { Name = $"dSHeuristics = {heur}", Type = AffectedObjectType.DomainSetting }]));
            var excluded = CheckLogic.AdminSdExMaskGroups(heur);
            if (excluded.Count > 0)
                findings.Add(NewFinding("AdminSdExMask", "Operator Groups Excluded From AdminSDHolder Protection", FindingSeverity.High,
                    excluded.Select(g => new AffectedObject { Name = g, Type = AffectedObjectType.DomainSetting, Detail = $"dSHeuristics = {heur}" }).ToList()));
        }

        // Smart-card accounts: their password hash never changes unless rotated
        var smart = QueryUsers(CheckFilters.SmartcardOldPassword(Ft(CheckFilters.SmartcardPasswordMaxDays))).Select(AffectedObject.FromUser).ToList();
        if (smart.Count > 0)
            findings.Add(NewFinding("SmartcardOldPassword", $"{smart.Count} Smart-Card Account(s) With an Unrotated Password Hash", FindingSeverity.Medium, smart));
        return findings;
    });
}
