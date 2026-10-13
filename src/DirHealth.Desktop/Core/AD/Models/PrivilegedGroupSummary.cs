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
