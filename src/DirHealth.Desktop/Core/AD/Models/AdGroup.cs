namespace DirHealth.Desktop.Core.AD.Models;

public class AdGroup
{
    public string Name           { get; set; } = "";
    public string Description    { get; set; } = "";
    public int MemberCount       { get; set; }
    public string DistinguishedName { get; set; } = "";
    public string GroupScope     { get; set; } = "";
}
