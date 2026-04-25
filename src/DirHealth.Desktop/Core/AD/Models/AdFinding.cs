namespace DirHealth.Desktop.Core.AD.Models;

public enum FindingSeverity { Low, Medium, High, Critical }

public class AdFinding
{
    public string Category    { get; set; } = "";
    public string Title       { get; set; } = "";
    public string Description { get; set; } = "";
    public FindingSeverity Severity { get; set; }
    public int Count          { get; set; }
    public List<string> AffectedObjects { get; set; } = [];
    public bool IsAcknowledged { get; set; }
    public string AcknowledgeNote { get; set; } = "";
}
