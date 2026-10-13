namespace DirHealth.Desktop.Core.AD;

/// <summary>Object type a prefixed name search is restricted to (S15-3).</summary>
public enum SearchScopeType { Any, User, Group, Computer, OrganizationalUnit }

/// <summary>
/// Parses the search prefixes "user:", "group:", "computer:", "ou:" (S15-3). A prefix turns
/// the query into a name search restricted to that object type; the rest of the text is
/// the search term and is escaped like any other name search. Pure, no LDAP.
/// </summary>
public static class SearchQuery
{
    private static readonly (string Prefix, SearchScopeType Type)[] Prefixes =
    [
        ("user:",     SearchScopeType.User),
        ("group:",    SearchScopeType.Group),
        ("computer:", SearchScopeType.Computer),
        ("ou:",       SearchScopeType.OrganizationalUnit),
    ];

    /// <summary>Returns the restricted type and the remaining term, or (Any, query) without a prefix.</summary>
    public static (SearchScopeType Type, string Term) Parse(string query)
    {
        var q = (query ?? "").TrimStart();
        foreach (var (prefix, type) in Prefixes)
            if (q.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return (type, q[prefix.Length..].Trim());
        return (SearchScopeType.Any, (query ?? "").Trim());
    }

    /// <summary>Object-class clause for a restricted search; empty for Any.</summary>
    public static string TypeClause(SearchScopeType type) => type switch
    {
        SearchScopeType.User               => "(objectCategory=person)(objectClass=user)",
        SearchScopeType.Group              => "(objectClass=group)",
        SearchScopeType.Computer           => "(objectClass=computer)",
        SearchScopeType.OrganizationalUnit => "(objectClass=organizationalUnit)",
        _                                  => "",
    };

    /// <summary>Name filter for an already escaped term, optionally restricted to a type.</summary>
    public static string NameFilter(string escapedTerm, SearchScopeType type)
    {
        var name = type == SearchScopeType.OrganizationalUnit
            ? $"(|(ou=*{escapedTerm}*)(description=*{escapedTerm}*))"
            : $"(|(cn=*{escapedTerm}*)(displayName=*{escapedTerm}*)(sAMAccountName=*{escapedTerm}*))";
        var typeClause = TypeClause(type);
        return typeClause.Length == 0 ? name : $"(&{typeClause}{name})";
    }
}
