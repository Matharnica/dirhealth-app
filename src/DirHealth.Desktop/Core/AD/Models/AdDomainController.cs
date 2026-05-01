namespace DirHealth.Desktop.Core.AD.Models;

public class AdDomainController
{
    public string       Name              { get; set; } = "";
    public string       OperatingSystem   { get; set; } = "";
    public string       OsVersion         { get; set; } = "";
    public DateTime?    LastLogon         { get; set; }
    public bool         IsGlobalCatalog   { get; set; }
    public List<string> FsmoRoles         { get; set; } = [];
    public bool         IsEol             { get; set; }
    public DateTime?    EolDate           { get; set; }
    public string       DistinguishedName { get; set; } = "";

    public string LastLogonDisplay =>
        LastLogon.HasValue ? $"{(int)(DateTime.UtcNow - LastLogon.Value.ToUniversalTime()).TotalDays} days ago" : "Never";

    public string EolInfo =>
        IsEol && EolDate.HasValue ? $"EOL since {EolDate.Value:MMM yyyy}" : "";
}
