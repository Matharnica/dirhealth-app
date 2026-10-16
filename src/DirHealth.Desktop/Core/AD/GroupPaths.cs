using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

/// <summary>
/// Membership paths for the user dossier (S15-5): "jdoe → IT-Admins → Domain Admins".
/// Breadth-first from the user's direct and primary groups along each group's memberOf, so
/// every group gets its shortest path. Cycles (A ↔ B) cannot loop: each group is visited
/// once. Pure, no LDAP.
/// </summary>
public static class GroupPaths
{
    public const string Arrow = " → ";

    public static void Assign(string userLabel, IReadOnlyList<AdUserGroup> groups)
    {
        var byDn  = groups.ToDictionary(g => g.DistinguishedName, StringComparer.OrdinalIgnoreCase);
        var chain = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<AdUserGroup>();

        foreach (var g in groups.Where(g => g.Kind is GroupMembershipKind.Direct or GroupMembershipKind.Primary))
        {
            chain[g.DistinguishedName] = [g.Kind == GroupMembershipKind.Primary ? $"{g.Name} (primary)" : g.Name];
            queue.Enqueue(g);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var parentDn in current.ParentDns)
            {
                if (chain.ContainsKey(parentDn) || !byDn.TryGetValue(parentDn, out var parent)) continue;
                chain[parentDn] = [.. chain[current.DistinguishedName], parent.Name];
                queue.Enqueue(parent);
            }
        }

        foreach (var g in groups)
            g.Path = chain.TryGetValue(g.DistinguishedName, out var steps)
                ? userLabel + Arrow + string.Join(Arrow, steps)
                : userLabel + Arrow + "…" + Arrow + g.Name;   // reachable only via an unread link
    }
}
