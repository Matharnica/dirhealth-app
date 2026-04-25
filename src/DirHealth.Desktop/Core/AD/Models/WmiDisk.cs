namespace DirHealth.Desktop.Core.AD.Models;

public class WmiDisk
{
    public string Drive      { get; set; } = "";
    public long   TotalBytes { get; set; }
    public long   FreeBytes  { get; set; }

    public double UsedPercent => TotalBytes > 0
        ? Math.Round((double)(TotalBytes - FreeBytes) / TotalBytes * 100, 1) : 0;

    public string TotalDisplay => FormatBytes(TotalBytes);
    public string FreeDisplay  => FormatBytes(FreeBytes);

    private static string FormatBytes(long b) =>
        b >= 1_073_741_824 ? $"{b / 1_073_741_824.0:F1} GB" : $"{b / 1_048_576.0:F0} MB";
}
