namespace DirHealth.Desktop.Core.AD.Models;

public class AdUser
{
    public string SamAccountName { get; set; } = "";
    public string DisplayName    { get; set; } = "";
    public string Email          { get; set; } = "";
    public DateTime? LastLogon   { get; set; }
    public DateTime? PasswordLastSet { get; set; }
    public DateTime? PasswordExpires { get; set; }
    public bool PasswordNeverExpires { get; set; }
    public bool IsEnabled        { get; set; }
    public string DistinguishedName { get; set; } = "";
    public int? DaysUntilPasswordExpiry { get; set; }
}
