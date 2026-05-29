namespace DirHealth.Desktop.Core.AD.Models;

public class PrivilegedGroupSummary
{
    public string       Name            { get; set; } = "";
    public string       RiskDescription { get; set; } = "";
    public int          MemberCount     { get; set; }
    public List<string> Members         { get; set; } = [];
}
