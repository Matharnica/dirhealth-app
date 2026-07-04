namespace DirHealth.Desktop.Core.AD.Models;

public class AdGpo
{
    public string DisplayName        { get; set; } = "";
    public string Guid               { get; set; } = "";   // cn, e.g. {31B2F340-...}
    public DateTime? WhenCreated     { get; set; }
    public DateTime? WhenChanged     { get; set; }
    public int    LinkCount          { get; set; }
    public string Status             { get; set; } = "Active"; // Active / Orphaned / Disabled

    public bool IsOrphaned => Status == "Orphaned";
    public bool IsDisabled => Status == "Disabled";
}
