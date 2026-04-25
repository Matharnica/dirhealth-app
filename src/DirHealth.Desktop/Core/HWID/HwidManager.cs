using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace DirHealth.Desktop.Core.HWID;

public static class HwidManager
{
    private static string? _cached;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    private const int SM_REMOTESESSION = 0x1000;

    public static string ComputeHWID()
    {
        if (_cached is not null) return _cached;

        var cpu  = GetWmiValue("Win32_Processor", "ProcessorId");
        var mb   = GetWmiValue("Win32_BaseBoard", "SerialNumber");
        var disk = GetWmiValue("Win32_DiskDrive", "SerialNumber");

        string raw;
        if (GetSystemMetrics(SM_REMOTESESSION) != 0)
        {
            // Per-user HWID on Terminal Server: each RDS user gets a unique identity
            var username = Environment.UserName;
            raw = $"{cpu}-{mb}-{disk}-{username}";
        }
        else
        {
            raw = $"{cpu}-{mb}-{disk}";
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        _cached = Convert.ToHexString(bytes);
        return _cached;
    }

    public static bool IsTerminalServer => GetSystemMetrics(SM_REMOTESESSION) != 0;

    internal static string GetWmiValue(string wmiClass, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT {property} FROM {wmiClass}");
            foreach (ManagementObject obj in searcher.Get())
            {
                var val = obj[property]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(val)) return val;
            }
        }
        catch { }
        return "UNKNOWN";
    }
}
