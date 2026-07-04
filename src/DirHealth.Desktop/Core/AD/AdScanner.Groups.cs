using System.DirectoryServices;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

// Organizational-unit and group queries (S12-3 split).
// Shared helpers (QueryGroups, GetString, GetLong, EscapeDn) live in AdScanner.cs.
public partial class AdScanner
{
    public async Task<List<AdOU>> GetAllOUsAsync()
    {
        return await Task.Run(() =>
        {
            var ous = new List<AdOU>();
            using var root     = _connector.GetRootEntry();
            using var searcher = _connector.CreateSearcher(root,
                "(objectClass=organizationalUnit)",
                "ou", "description", "distinguishedName");
            using var results = searcher.FindAll();
            foreach (SearchResult r in results)
            {
                ous.Add(new AdOU
                {
                    Name              = GetString(r.Properties, "ou"),
                    DistinguishedName = GetString(r.Properties, "distinguishedName"),
                    Description       = GetString(r.Properties, "description"),
                });
            }
            return ous.OrderBy(o => o.Name).ToList();
        });
    }

    public async Task<(int Users, int Computers, int Groups)> GetOUCountsAsync(string distinguishedName)
    {
        return await Task.Run(() => (
            CountObjectsInOU(distinguishedName, "(&(objectClass=user)(objectCategory=person))"),
            CountObjectsInOU(distinguishedName, "(objectClass=computer)"),
            CountObjectsInOU(distinguishedName, "(objectClass=group)")
        ));
    }

    private int CountObjectsInOU(string ouDn, string filter)
    {
        try
        {
            using var entry    = _connector.GetEntry($"LDAP://{ouDn}");
            using var searcher = new DirectorySearcher(entry)
            {
                Filter      = filter,
                PageSize    = 1000,
                SearchScope = SearchScope.OneLevel
            };
            using var results = searcher.FindAll();
            return results.Count;
        }
        catch { return 0; }
    }

    public async Task<List<AdGroup>> GetAllGroupsWithCountAsync()
    {
        return await Task.Run(() => QueryGroups("(objectClass=group)"));
    }

    public async Task<AdGroupDetail> GetGroupDetailAsync(string distinguishedName)
    {
        return await Task.Run(() =>
        {
            var detail = new AdGroupDetail { DistinguishedName = distinguishedName };
            try
            {
                var escaped = EscapeDn(distinguishedName);
                using var root = _connector.GetRootEntry();

                // Read base group properties
                using var searcher = _connector.CreateSearcher(root,
                    $"(distinguishedName={escaped})",
                    "cn", "description", "groupType");
                var result = searcher.FindOne();
                if (result is null) return detail;

                detail.Name        = GetString(result.Properties, "cn");
                detail.Description = GetString(result.Properties, "description");

                var gt = GetLong(result.Properties, "groupType");
                detail.GroupScope = (gt & 0x8) != 0 ? "Universal"
                                  : (gt & 0x4) != 0 ? "Local"
                                  :                   "Global";

                // Range retrieval for member attribute (AD limits per-page)
                var memberDns = new List<string>();
                int start = 0;
                while (true)
                {
                    var rangeAttr = $"member;range={start}-*";
                    using var rs = _connector.CreateSearcher(root,
                        $"(distinguishedName={escaped})", rangeAttr);
                    var rr = rs.FindOne();
                    if (rr is null) break;

                    bool lastPage = false;
                    int count = 0;
                    foreach (string key in rr.Properties.PropertyNames)
                    {
                        if (!key.StartsWith("member;range=", StringComparison.OrdinalIgnoreCase)) continue;
                        lastPage = key.EndsWith("-*", StringComparison.OrdinalIgnoreCase);
                        foreach (var v in rr.Properties[key])
                        {
                            memberDns.Add(v?.ToString() ?? "");
                            count++;
                        }
                        break;
                    }
                    if (lastPage || count == 0) break;
                    start += count;
                }

                detail.Members.AddRange(BatchResolveGroupMembers(root, memberDns));
            }
            catch { }
            return detail;
        });
    }

    private List<AdGroupMember> BatchResolveGroupMembers(DirectoryEntry root, List<string> memberDns)
    {
        var members = new List<AdGroupMember>(memberDns.Count);
        const int batchSize = 50;
        for (int i = 0; i < memberDns.Count; i += batchSize)
        {
            var batch  = memberDns.Skip(i).Take(batchSize).ToList();
            var filter = "(|" + string.Join("", batch.Select(dn => $"(distinguishedName={EscapeDn(dn)})")) + ")";
            try
            {
                using var searcher = _connector.CreateSearcher(root, filter,
                    "cn", "sAMAccountName", "objectClass", "distinguishedName");
                using var results = searcher.FindAll();
                var found = new Dictionary<string, AdGroupMember>(StringComparer.OrdinalIgnoreCase);
                foreach (SearchResult r in results)
                {
                    var dn       = GetString(r.Properties, "distinguishedName");
                    var objClass = r.Properties["objectClass"].Cast<string>().ToList();
                    var type     = objClass.Contains("group")    ? "Group"
                                 : objClass.Contains("computer") ? "Computer"
                                 :                                 "User";
                    found[dn] = new AdGroupMember
                    {
                        Name              = GetString(r.Properties, "cn"),
                        SamAccountName    = GetString(r.Properties, "sAMAccountName"),
                        ObjectType        = type,
                        DistinguishedName = dn,
                    };
                }
                foreach (var dn in batch)
                    members.Add(found.TryGetValue(dn, out var m)
                        ? m
                        : new AdGroupMember { Name = dn, DistinguishedName = dn });
            }
            catch
            {
                foreach (var dn in batch)
                    members.Add(new AdGroupMember { Name = dn, DistinguishedName = dn });
            }
        }
        return members;
    }

    public async Task<AdGroupDetail> GetDomainAdminsAsync()
    {
        var dn = await Task.Run(() =>
        {
            try
            {
                using var root     = _connector.GetRootEntry();
                using var searcher = _connector.CreateSearcher(root,
                    "(&(objectClass=group)(cn=Domain Admins))", "distinguishedName");
                var result = searcher.FindOne();
                return result is not null ? GetString(result.Properties, "distinguishedName") : "";
            }
            catch { return ""; }
        });

        if (string.IsNullOrEmpty(dn))
            return new AdGroupDetail { Name = "Domain Admins", Description = "Group not found." };

        return await GetGroupDetailAsync(dn);
    }
}
