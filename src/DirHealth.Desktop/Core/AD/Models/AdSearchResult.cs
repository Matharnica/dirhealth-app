namespace DirHealth.Desktop.Core.AD.Models;

public enum AdObjectType { User, Computer, Group, OrganizationalUnit }

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
        AdObjectType.OrganizationalUnit => "OU",
        _                    => ""
    };

    /// <summary>Icon for the type column (S15-1); the tooltip carries <see cref="TypeLabel"/>.</summary>
    public string TypeIcon => ObjectType switch
    {
        AdObjectType.User               => "👤",
        AdObjectType.Computer           => "🖥",
        AdObjectType.Group              => "👥",
        AdObjectType.OrganizationalUnit => "📁",
        _                               => "",
    };
}
