namespace DirHealth.Desktop.Core.AD.Models;

public class WmiEventLogEntry
{
    public DateTime? TimeGenerated { get; set; }
    public string    Level         { get; set; } = "";
    public string    Source        { get; set; } = "";
    public string    Message       { get; set; } = "";
}
