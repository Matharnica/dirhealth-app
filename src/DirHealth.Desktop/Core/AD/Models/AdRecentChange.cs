namespace DirHealth.Desktop.Core.AD.Models;

public class AdRecentChange
{
    public string   Name              { get; set; } = "";
    public string   ObjectType        { get; set; } = ""; // "User" or "Computer"
    public string   Action            { get; set; } = ""; // "Created" or "Modified"
    public DateTime ChangedAt         { get; set; }
    public string   DistinguishedName { get; set; } = "";

    public string ChangedAtDisplay => ChangedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string TypeIcon         => ObjectType == "Computer" ? "🖥" : "👤";
}
