namespace DirHealth.Desktop.Core.AD.Models;

public class AdDomainTrust
{
    public string Name            { get; set; } = "";
    public string TrustType       { get; set; } = "";
    public string Direction       { get; set; } = "";
    public bool   IsForestTrust   { get; set; }
    public bool   IsBidirectional => Direction == "Bidirectional";
}
