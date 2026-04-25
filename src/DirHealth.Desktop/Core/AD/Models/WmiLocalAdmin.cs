namespace DirHealth.Desktop.Core.AD.Models;

public class WmiLocalAdmin
{
    public string Name   { get; set; } = "";
    public string Domain { get; set; } = "";
    public string Full   => string.IsNullOrEmpty(Domain) ? Name : $"{Domain}\\{Name}";
}
