using System.DirectoryServices;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

public enum SearchMode { Name, Sid, Email, Ou, Ldap }

public class AdSearcher
{
    private readonly AdConnector _connector;

    public AdSearcher(AdConnector connector)
    {
        _connector = connector;
    }

    public async Task<List<AdSearchResult>> SearchAsync(string query, SearchMode mode)
    {
        return await Task.Run(() => mode switch
        {
            SearchMode.Name  => SearchByName(query),
            SearchMode.Sid   => SearchBySid(query),
            SearchMode.Email => SearchByEmail(query),
            SearchMode.Ou    => SearchByOu(query),
            SearchMode.Ldap  => SearchByLdap(query),
            _                => new List<AdSearchResult>()
        });
    }

    private List<AdSearchResult> SearchByName(string query)
    {
        var escaped = Escape(query);
        var filter  = $"(|(cn=*{escaped}*)(displayName=*{escaped}*)(sAMAccountName=*{escaped}*))";
        return RunSearch(filter);
    }

    private List<AdSearchResult> SearchBySid(string query)
    {
        return RunSearch($"(objectSid={query})");
    }

    private List<AdSearchResult> SearchByEmail(string query)
    {
        var escaped = Escape(query);
        return RunSearch($"(|(mail=*{escaped}*)(userPrincipalName=*{escaped}*))");
    }

    private List<AdSearchResult> SearchByOu(string query)
    {
        var results = new List<AdSearchResult>();
        try
        {
            using var ouEntry  = _connector.GetEntry($"LDAP://{query}");
            using var searcher = new DirectorySearcher(ouEntry,
                "(|(objectClass=user)(objectClass=computer)(objectClass=group))",
                new[] { "cn", "displayName", "sAMAccountName", "mail", "objectSid",
                        "distinguishedName", "objectClass", "userAccountControl" })
            {
                SearchScope = SearchScope.OneLevel,
                PageSize    = 500
            };
            using var sr = searcher.FindAll();
            foreach (SearchResult r in sr)
                results.Add(MapResult(r));
        }
        catch { }
        return results;
    }

    private List<AdSearchResult> SearchByLdap(string query)
    {
        return RunSearch(query);
    }

    private List<AdSearchResult> RunSearch(string filter)
    {
        var results = new List<AdSearchResult>();
        try
        {
            using var root    = _connector.GetRootEntry();
            using var searcher = _connector.CreateSearcher(root, filter,
                "cn", "displayName", "sAMAccountName", "mail", "objectSid",
                "distinguishedName", "objectClass", "userAccountControl");
            searcher.SizeLimit = 500;
            using var found = searcher.FindAll();
            foreach (SearchResult r in found)
                results.Add(MapResult(r));
        }
        catch { }
        return results;
    }

    private static AdSearchResult MapResult(SearchResult r)
    {
        var props   = r.Properties;
        var classes = props["objectClass"];
        var type    = AdObjectType.User;
        for (int i = 0; i < classes.Count; i++)
        {
            var c = classes[i]?.ToString() ?? "";
            if (c == "computer") { type = AdObjectType.Computer; break; }
            if (c == "group")    { type = AdObjectType.Group;    break; }
        }

        var dn = GetString(props, "distinguishedName");
        var ou = ExtractOu(dn);

        return new AdSearchResult
        {
            ObjectType        = type,
            Name              = GetString(props, "cn"),
            DisplayName       = GetString(props, "displayName"),
            SamAccountName    = GetString(props, "sAMAccountName"),
            Email             = GetString(props, "mail"),
            Sid               = GetSid(props),
            DistinguishedName = dn,
            OU                = ou,
            IsEnabled         = (GetLong(props, "userAccountControl") & 2) == 0,
        };
    }

    private static string ExtractOu(string dn)
    {
        var parts = dn.Split(',');
        return parts.Length > 1 ? string.Join(",", parts.Skip(1)) : dn;
    }

    private static string GetString(ResultPropertyCollection props, string name)
        => props[name].Count > 0 ? props[name][0]?.ToString() ?? "" : "";

    private static long GetLong(ResultPropertyCollection props, string name)
    {
        if (props[name].Count == 0) return 0;
        var val = props[name][0];
        return val is int i ? i : val is long l ? l : 0;
    }

    private static string GetSid(ResultPropertyCollection props)
    {
        if (props["objectSid"].Count == 0) return "";
        try
        {
            var sidBytes = (byte[])props["objectSid"][0];
            return new System.Security.Principal.SecurityIdentifier(sidBytes, 0).ToString();
        }
        catch { return ""; }
    }

    private static string Escape(string input) =>
        input.Replace("\\", "\\5c").Replace("*", "\\2a")
             .Replace("(", "\\28").Replace(")", "\\29")
             .Replace("\0", "\\00");
}
