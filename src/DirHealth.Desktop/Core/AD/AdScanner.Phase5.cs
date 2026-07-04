using System.DirectoryServices;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

// Phase 5 queries — GPO Browser (S10-1) and AD Data Quality Report (S10-2).
public partial class AdScanner
{
    private string ResolveDomainDn()
    {
        try
        {
            var dsePath = string.IsNullOrEmpty(_connector.Domain)
                ? "LDAP://RootDSE"
                : $"LDAP://{_connector.Domain}/RootDSE";
            using var dse = _connector.GetEntry(dsePath);
            return dse.Properties["defaultNamingContext"]?[0]?.ToString() ?? "";
        }
        catch { return ""; }
    }

    public async Task<List<AdGpo>> GetAllGposAsync()
    {
        return await Task.Run(() =>
        {
            var gpos = new List<AdGpo>();
            var domainDn = ResolveDomainDn();
            if (domainDn.Length == 0) return gpos;

            // Stage 1 — count how often each GPO GUID is referenced by a gPLink
            // (domain root, OUs and sites all live under the subtree searched from the domain root).
            var linkCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var root         = _connector.GetRootEntry();
                using var linkSearcher = _connector.CreateSearcher(root, "(gPLink=*)", "gPLink");
                using var linkResults  = linkSearcher.FindAll();
                foreach (SearchResult r in linkResults)
                {
                    var gpLink = GetString(r.Properties, "gPLink");
                    foreach (var guid in GpoLogic.ExtractGuids(gpLink))
                        linkCounts[guid] = linkCounts.TryGetValue(guid, out var c) ? c + 1 : 1;
                }
            }
            catch { }

            // Stage 2 — enumerate the GPO objects and join against the link counts.
            try
            {
                using var policies = GetEntryByDn($"CN=Policies,CN=System,{domainDn}");
                using var searcher = new DirectorySearcher(policies)
                {
                    Filter      = "(objectClass=groupPolicyContainer)",
                    PageSize    = 1000,
                    SearchScope = SearchScope.OneLevel,
                };
                searcher.PropertiesToLoad.AddRange(new[]
                {
                    "cn", "displayName", "whenCreated", "whenChanged", "flags",
                });
                using var results = searcher.FindAll();
                foreach (SearchResult r in results)
                {
                    var props = r.Properties;
                    var guid  = GetString(props, "cn");
                    int links = linkCounts.TryGetValue(guid, out var c) ? c : 0;
                    int flags = (int)GetLong(props, "flags");

                    gpos.Add(new AdGpo
                    {
                        DisplayName = GetString(props, "displayName"),
                        Guid        = guid,
                        WhenCreated = GetAdDateTime(props, "whenCreated"),
                        WhenChanged = GetAdDateTime(props, "whenChanged"),
                        LinkCount   = links,
                        Status      = GpoLogic.Classify(flags, links),
                    });
                }
            }
            catch { }

            return gpos
                .OrderByDescending(g => g.IsOrphaned)   // surface cleanup candidates first
                .ThenBy(g => g.DisplayName)
                .ToList();
        });
    }

    public record DataQualityResult(int TotalUsers, List<AdAttributeCompleteness> Attributes);

    // (ldap attribute, display label) — the master-data fields an admin expects to be populated.
    private static readonly (string Ldap, string Label)[] DataQualityAttributes =
    [
        ("mail",                       "Email"),
        ("telephoneNumber",            "Phone"),
        ("department",                 "Department"),
        ("title",                      "Job Title"),
        ("manager",                    "Manager"),
        ("physicalDeliveryOfficeName", "Office"),
    ];

    public async Task<DataQualityResult> GetDataQualityAsync()
    {
        return await Task.Run(() =>
        {
            var filled = DataQualityAttributes
                .ToDictionary(a => a.Ldap, _ => 0, StringComparer.OrdinalIgnoreCase);
            int total = 0;

            try
            {
                using var root     = _connector.GetRootEntry();
                using var searcher = _connector.CreateSearcher(root,
                    "(&(objectClass=user)(objectCategory=person)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",
                    DataQualityAttributes.Select(a => a.Ldap).ToArray());
                using var results = searcher.FindAll();
                foreach (SearchResult r in results)
                {
                    total++;
                    foreach (var (ldap, _) in DataQualityAttributes)
                    {
                        var props = r.Properties[ldap];
                        if (props.Count > 0 && !string.IsNullOrWhiteSpace(props[0]?.ToString()))
                            filled[ldap]++;
                    }
                }
            }
            catch { }

            var attributes = DataQualityAttributes
                .Select(a => new AdAttributeCompleteness
                {
                    AttributeName = a.Label,
                    LdapName      = a.Ldap,
                    FilledCount   = filled[a.Ldap],
                    TotalCount    = total,
                })
                .OrderBy(a => a.Percent)          // worst completeness first
                .ThenBy(a => a.AttributeName)
                .ToList();

            return new DataQualityResult(total, attributes);
        });
    }
}
