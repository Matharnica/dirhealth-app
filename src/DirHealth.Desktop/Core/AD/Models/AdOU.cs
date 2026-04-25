namespace DirHealth.Desktop.Core.AD.Models;

public class AdOU
{
    public string Name              { get; set; } = "";
    public string DistinguishedName { get; set; } = "";
    public string Description       { get; set; } = "";
    public int    UserCount         { get; set; }
    public int    ComputerCount     { get; set; }
    public int    GroupCount        { get; set; }
}
