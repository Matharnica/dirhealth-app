using System.Management;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

public class AdWmiClient
{
    public async Task<bool> PingAsync(string hostname)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var ping  = new System.Net.NetworkInformation.Ping();
                var reply = ping.Send(hostname, 2000);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success;
            }
            catch { return false; }
        });
    }

    public async Task<long> PingTimeMs(string hostname)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var ping  = new System.Net.NetworkInformation.Ping();
                var reply = ping.Send(hostname, 2000);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success
                    ? reply.RoundtripTime : -1L;
            }
            catch { return -1L; }
        });
    }

    public async Task<List<WmiDisk>> GetDisksAsync(string hostname)
    {
        return await Task.Run(() =>
        {
            var disks = new List<WmiDisk>();
            try
            {
                var scope = new ManagementScope($@"\\{hostname}\root\cimv2");
                scope.Connect();
                var query = new ObjectQuery(
                    "SELECT DeviceID, Size, FreeSpace FROM Win32_LogicalDisk WHERE DriveType=3");
                using var s = new ManagementObjectSearcher(scope, query);
                foreach (ManagementObject obj in s.Get())
                {
                    disks.Add(new WmiDisk
                    {
                        Drive      = obj["DeviceID"]?.ToString() ?? "",
                        TotalBytes = obj["Size"]      is ulong t ? (long)t : 0,
                        FreeBytes  = obj["FreeSpace"] is ulong f ? (long)f : 0,
                    });
                }
            }
            catch { }
            return disks;
        });
    }

    public async Task<List<WmiLocalAdmin>> GetLocalAdminsAsync(string hostname)
    {
        return await Task.Run(() =>
        {
            var admins = new List<WmiLocalAdmin>();
            try
            {
                var scope = new ManagementScope($@"\\{hostname}\root\cimv2");
                scope.Connect();
                var query = new ObjectQuery(
                    "SELECT PartComponent FROM Win32_GroupUser WHERE GroupComponent=\"Win32_Group.Domain='" +
                    hostname + "',Name='Administrators'\"");
                using var s = new ManagementObjectSearcher(scope, query);
                foreach (ManagementObject obj in s.Get())
                {
                    var part   = obj["PartComponent"]?.ToString() ?? "";
                    var name   = Extract(part, "Name");
                    var domain = Extract(part, "Domain");
                    admins.Add(new WmiLocalAdmin { Name = name, Domain = domain });
                }
            }
            catch { }
            return admins;
        });
    }

    public async Task<List<WmiLoggedOnUser>> GetLoggedOnUsersAsync(string hostname)
    {
        return await Task.Run(() =>
        {
            var users = new List<WmiLoggedOnUser>();
            try
            {
                var scope = new ManagementScope($@"\\{hostname}\root\cimv2");
                scope.Connect();
                var query = new ObjectQuery(
                    "SELECT Antecedent FROM Win32_LoggedOnUser");
                using var s = new ManagementObjectSearcher(scope, query);
                foreach (ManagementObject obj in s.Get())
                {
                    var antecedent = obj["Antecedent"]?.ToString() ?? "";
                    var name       = Extract(antecedent, "Name");
                    var domain     = Extract(antecedent, "Domain");
                    if (!string.IsNullOrEmpty(name))
                        users.Add(new WmiLoggedOnUser { Name = name, Domain = domain });
                }
            }
            catch { }
            return users.DistinctBy(u => u.Full).ToList();
        });
    }

    public async Task<List<WmiEventLogEntry>> GetEventLogAsync(
        string hostname, string logName, string? severityFilter, int maxEntries)
    {
        return await Task.Run(() =>
        {
            var entries = new List<WmiEventLogEntry>();
            try
            {
                var scope = new ManagementScope($@"\\{hostname}\root\cimv2");
                scope.Connect();

                var typeFilter = severityFilter switch
                {
                    "Error"       => " AND Type='error'",
                    "Warning"     => " AND Type='warning'",
                    "Information" => " AND Type='information'",
                    _             => ""
                };
                var query = new ObjectQuery(
                    $"SELECT TimeGenerated, Type, SourceName, Message FROM Win32_NTLogEvent " +
                    $"WHERE Logfile='{logName}'{typeFilter}");
                using var s = new ManagementObjectSearcher(scope, query);
                s.Options.ReturnImmediately = true;

                foreach (ManagementObject obj in s.Get())
                {
                    if (entries.Count >= maxEntries) break;
                    var raw = obj["TimeGenerated"]?.ToString();
                    DateTime? dt = raw is not null
                        ? ManagementDateTimeConverter.ToDateTime(raw) : null;
                    entries.Add(new WmiEventLogEntry
                    {
                        TimeGenerated = dt,
                        Level         = obj["Type"]?.ToString() ?? "",
                        Source        = obj["SourceName"]?.ToString() ?? "",
                        Message       = obj["Message"]?.ToString()?.Split('\n')[0] ?? "",
                    });
                }
            }
            catch { }
            return entries;
        });
    }

    private static string Extract(string wmiRef, string key)
    {
        var prefix = $"{key}=\"";
        var start  = wmiRef.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return "";
        start += prefix.Length;
        var end = wmiRef.IndexOf('"', start);
        return end > start ? wmiRef[start..end] : "";
    }
}
