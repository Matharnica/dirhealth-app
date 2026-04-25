namespace DirHealth.Desktop.Core.AD.Models;

public class AdComputer
{
    public string Name           { get; set; } = "";
    public string OperatingSystem { get; set; } = "";
    public string OsVersion      { get; set; } = "";
    public DateTime? LastLogon   { get; set; }
    public bool IsEnabled        { get; set; }
    public string DistinguishedName { get; set; } = "";
}
