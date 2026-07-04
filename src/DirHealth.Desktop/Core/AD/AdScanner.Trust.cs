using System.DirectoryServices;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

// Trust, SID-history and recent-change (timeline) queries (S12-3 split).
// Shared helpers (QueryUsers, GetString, GetLong, GetEntryByDn) live in AdScanner.cs.
public partial class AdScanner
{
    public async Task<List<AdDomainTrust>> GetDomainTrustsAsync()
    {
        return await Task.Run(() =>
        {
            var trusts = new List<AdDomainTrust>();
            string domainDn = "";
            try
            {
                var dsePath = string.IsNullOrEmpty(_connector.Domain)
                    ? "LDAP://RootDSE"
                    : $"LDAP://{_connector.Domain}/RootDSE";
                using var dse = _connector.GetEntry(dsePath);
                domainDn = dse.Properties["defaultNamingContext"]?[0]?.ToString() ?? "";
            }
            catch { }

            if (domainDn.Length == 0) return trusts;

            try
            {
                using var systemEntry = GetEntryByDn($"CN=System,{domainDn}");
                using var searcher = _connector.CreateSearcher(systemEntry,
                    "(objectClass=trustedDomain)",
                    "cn", "trustType", "trustDirection", "trustAttributes");
                searcher.SearchScope = SearchScope.OneLevel;
                using var results = searcher.FindAll();

                foreach (SearchResult r in results)
                {
                    var props      = r.Properties;
                    var trustType  = (int)GetLong(props, "trustType");
                    var direction  = (int)GetLong(props, "trustDirection");
                    var attributes = (int)GetLong(props, "trustAttributes");

                    trusts.Add(new AdDomainTrust
                    {
                        Name          = GetString(props, "cn"),
                        TrustType     = trustType switch
                        {
                            1 => "Downlevel (NT4)",
                            2 => "AD / Kerberos",
                            3 => "MIT Kerberos",
                            _ => $"Unknown ({trustType})"
                        },
                        Direction     = direction switch
                        {
                            1 => "Inbound",
                            2 => "Outbound",
                            3 => "Bidirectional",
                            _ => "Unknown"
                        },
                        IsForestTrust = (attributes & 8) != 0,
                    });
                }
            }
            catch { }

            return trusts.OrderBy(t => t.Name).ToList();
        });
    }

    public async Task<List<AdUser>> GetSidHistoryAccountsAsync()
    {
        return await Task.Run(() =>
            QueryUsers("(&(objectClass=user)(objectCategory=person)" +
                       "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                       "(sIDHistory=*))"));
    }

    public async Task<List<AdRecentChange>> GetRecentChangesAsync(int days = 30)
    {
        return await Task.Run(() =>
        {
            var changes  = new List<AdRecentChange>();
            var cutoff   = DateTime.UtcNow.AddDays(-days);
            var ldapTime = cutoff.ToString("yyyyMMddHHmmss.0") + "Z";
            var filter   = $"(|(&(objectClass=user)(objectCategory=person)(whenChanged>={ldapTime}))" +
                           $"(&(objectClass=computer)(whenChanged>={ldapTime})))";

            using var root     = _connector.GetRootEntry();
            using var searcher = _connector.CreateSearcher(root, filter,
                "cn", "objectClass", "whenCreated", "whenChanged", "distinguishedName");

            using var results = searcher.FindAll();
            foreach (SearchResult r in results)
            {
                var props  = r.Properties;
                var objClasses = props["objectClass"];
                bool isComputer = false;
                for (int i = 0; i < objClasses.Count; i++)
                    if (objClasses[i]?.ToString() == "computer") { isComputer = true; break; }

                var whenCreated = GetAdDateTime(props, "whenCreated");
                var whenChanged = GetAdDateTime(props, "whenChanged");
                if (whenChanged is null) continue;

                var isNew = whenCreated.HasValue
                    && (whenChanged.Value - whenCreated.Value).TotalMinutes < 5;

                changes.Add(new AdRecentChange
                {
                    Name              = GetString(props, "cn"),
                    ObjectType        = isComputer ? "Computer" : "User",
                    Action            = isNew ? "Created" : "Modified",
                    ChangedAt         = whenChanged.Value,
                    DistinguishedName = GetString(props, "distinguishedName"),
                });
            }

            return changes.OrderByDescending(c => c.ChangedAt).ToList();
        });
    }

    // Used by GetRecentChangesAsync and by the GPO query in AdScanner.Phase5.cs.
    private static DateTime? GetAdDateTime(ResultPropertyCollection props, string name)
    {
        if (props[name].Count == 0) return null;
        var val = props[name][0];
        if (val is DateTime dt) return dt.ToUniversalTime();
        return null;
    }
}
