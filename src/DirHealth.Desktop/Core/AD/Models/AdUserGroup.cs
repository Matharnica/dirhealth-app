namespace DirHealth.Desktop.Core.AD.Models;

/// <summary>How a user holds a group (S13-2).</summary>
public enum GroupMembershipKind
{
    /// <summary>Listed in the group's member attribute.</summary>
    Direct,
    /// <summary>The account's primaryGroupID (not stored in member/memberOf).</summary>
    Primary,
    /// <summary>Reached through another group.</summary>
    Nested,
}

public class AdUserGroup
{
    public string              Name              { get; init; } = "";
    public string              DistinguishedName { get; init; } = "";
    public GroupMembershipKind Kind              { get; init; }

    public string KindLabel => Kind switch
    {
        GroupMembershipKind.Direct  => "direct",
        GroupMembershipKind.Primary => "primary",
        _                           => "nested",
    };

    /// <summary>
    /// Merges the three LDAP answers into one list, each group once. Precedence:
    /// Direct over Primary over Nested — a group that is both direct and reachable
    /// through nesting is shown as direct, because removing the direct link is the
    /// actionable fact. Sorted direct/primary first, then by name.
    /// </summary>
    public static List<AdUserGroup> Merge(
        IEnumerable<(string Dn, string Name)> direct,
        (string Dn, string Name)?             primary,
        IEnumerable<(string Dn, string Name)> transitive)
    {
        var byDn = new Dictionary<string, AdUserGroup>(StringComparer.OrdinalIgnoreCase);

        void Add((string Dn, string Name) g, GroupMembershipKind kind)
        {
            if (string.IsNullOrEmpty(g.Dn) || byDn.ContainsKey(g.Dn)) return;
            byDn[g.Dn] = new AdUserGroup { DistinguishedName = g.Dn, Name = g.Name, Kind = kind };
        }

        foreach (var g in direct) Add(g, GroupMembershipKind.Direct);
        if (primary is { } p)     Add(p, GroupMembershipKind.Primary);
        foreach (var g in transitive) Add(g, GroupMembershipKind.Nested);

        return byDn.Values
            .OrderBy(g => g.Kind == GroupMembershipKind.Nested ? 1 : 0)
            .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
