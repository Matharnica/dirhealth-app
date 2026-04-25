namespace DirHealth.Desktop.Core.AD.Models;

public enum AdObjectType { User, Computer, Group }

public class AdSearchResult
{
    public AdObjectType ObjectType        { get; set; }
    public string Name                    { get; set; } = "";
    public string DisplayName             { get; set; } = "";
    public string SamAccountName          { get; set; } = "";
    public string Email                   { get; set; } = "";
    public string Sid                     { get; set; } = "";
    public string DistinguishedName       { get; set; } = "";
    public string OU                      { get; set; } = "";
    public bool   IsEnabled               { get; set; }

    public string TypeLabel => ObjectType switch
    {
        AdObjectType.User     => "User",
        AdObjectType.Computer => "Computer",
        AdObjectType.Group    => "Group",
        _                    => ""
    };
}
