namespace DirHealth.Desktop.Core.AD.Models;

public class AdGroupMember
{
    public string Name              { get; set; } = "";
    public string SamAccountName   { get; set; } = "";
    public string ObjectType       { get; set; } = "";
    public string DistinguishedName { get; set; } = "";
}

public class AdGroupDetail
{
    public string              Name              { get; set; } = "";
    public string              Description       { get; set; } = "";
    public string              DistinguishedName { get; set; } = "";
    public string              GroupScope        { get; set; } = "";
    public List<AdGroupMember> Members           { get; set; } = [];
}
