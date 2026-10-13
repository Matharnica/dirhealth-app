namespace DirHealth.Desktop.Core.AD.Models;

public class PrivilegedGroupSummary
{
    public string       Name            { get; set; } = "";
    public string       RiskDescription { get; set; } = "";
    public int          MemberCount     { get; set; }
    public List<string> Members         { get; set; } = [];
    /// <summary>Set when the group could not be resolved (S13-1); null = found.</summary>
    public string?      NotFoundReason  { get; set; }
    public bool         IsNotFound      => NotFoundReason is not null;
}

/// <summary>One row of the privileged-groups table (S14-2): a membership, or the group
/// itself when it has no members or was not found — so empty groups stay visible.</summary>
public record PrivilegedMembershipRow(string Group, string Member, string Risk, string Status)
{
    public static IEnumerable<PrivilegedMembershipRow> From(IEnumerable<PrivilegedGroupSummary> groups)
    {
        foreach (var g in groups)
        {
            if (g.IsNotFound)            { yield return new(g.Name, "", g.RiskDescription, $"Not found: {g.NotFoundReason}"); continue; }
            if (g.Members.Count == 0)    { yield return new(g.Name, "", g.RiskDescription, "No members"); continue; }
            foreach (var m in g.Members) yield return new(g.Name, m, g.RiskDescription, "");
        }
    }
}
