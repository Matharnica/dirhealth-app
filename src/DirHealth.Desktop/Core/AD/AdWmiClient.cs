using System.Management;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

/// <summary>
/// Read-only WMI queries against one computer. Fix H1 (2026-11-16):
/// only FQDNs (from dNSHostName) are accepted, every scope uses an explicit Kerberos authority
/// (<see cref="WmiAuthority"/>) with Impersonate + PacketPrivacy — no negotiated NTLM fallback —
/// and failures throw instead of returning empty lists, so the view can show why.
/// The caller (ComputerDetailViewModel) only calls this on an explicit button click.
/// </summary>
public class AdWmiClient : IWmiClient
{
    private readonly Func<string> _principalDomain;

    /// <param name="principalDomain">
    /// Domain part of the Kerberos server principal. Default: the domain of the Windows account
    /// running DirHealth — WMI authenticates with that account's token.
    /// </param>
    public AdWmiClient(Func<string>? principalDomain = null)
    {
        _principalDomain = principalDomain ?? (() => Environment.UserDomainName);
    }

    private static bool IsValidHostname(string hostname) => WmiAuthority.IsValidFqdn(hostname);

    private ManagementScope Connect(string fqdn)
    {
        var options = new ConnectionOptions
        {
            Authority      = WmiAuthority.Build(fqdn, _principalDomain()),
            Impersonation  = ImpersonationLevel.Impersonate,
            Authentication = AuthenticationLevel.PacketPrivacy,
        };
        var scope = new ManagementScope($@"\\{fqdn}\root\cimv2", options);
        scope.Connect();
        return scope;
    }

    public async Task<bool> PingAsync(string hostname) => await PingTimeMs(hostname) >= 0;

    /// <summary>Round-trip time in ms, -1 if the host does not answer or the name is not an FQDN.</summary>
    public async Task<long> PingTimeMs(string hostname)
    {
        if (!IsValidHostname(hostname)) return -1L;
        return await Task.Run(() =>
        {
            try
            {
                using var ping  = new System.Net.NetworkInformation.Ping();
                var reply = ping.Send(hostname, 2000);
                return reply.Status == System.Net.NetworkInformation.IPStatus.Success
                    ? reply.RoundtripTime : -1L;
            }
            // No reply / name not resolvable = offline; the view says so. No credentials involved.
            catch (System.Net.NetworkInformation.PingException) { return -1L; }
        });
    }

    public async Task<List<WmiDisk>> GetDisksAsync(string hostname)
    {
        if (!IsValidHostname(hostname)) return [];
        return await Task.Run(() =>
        {
            var disks = new List<WmiDisk>();
            var scope = Connect(hostname);
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
            return disks;
        });
    }

    public async Task<List<WmiLocalAdmin>> GetLocalAdminsAsync(string hostname)
    {
        if (!IsValidHostname(hostname)) return [];
        return await Task.Run(() =>
        {
            var admins = new List<WmiLocalAdmin>();
            var scope = Connect(hostname);
            // Win32_Group.Domain of a local group is the computer name, not the FQDN.
            var safeHost = WmiAuthority.HostLabel(hostname);
            var query = new ObjectQuery(
                "SELECT PartComponent FROM Win32_GroupUser WHERE GroupComponent=\"Win32_Group.Domain='" +
                safeHost + "',Name='Administrators'\"");
            using var s = new ManagementObjectSearcher(scope, query);
            foreach (ManagementObject obj in s.Get())
            {
                var part   = obj["PartComponent"]?.ToString() ?? "";
                var name   = Extract(part, "Name");
                var domain = Extract(part, "Domain");
                admins.Add(new WmiLocalAdmin { Name = name, Domain = domain });
            }
            return admins;
        });
    }

    public async Task<List<WmiLoggedOnUser>> GetLoggedOnUsersAsync(string hostname)
    {
        if (!IsValidHostname(hostname)) return [];
        return await Task.Run(() =>
        {
            var users = new List<WmiLoggedOnUser>();
            var scope = Connect(hostname);
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
            return users.DistinctBy(u => u.Full).ToList();
        });
    }

    public async Task<List<WmiEventLogEntry>> GetEventLogAsync(
        string hostname, string logName, string? severityFilter, int maxEntries)
    {
        if (logName is not ("System" or "Security" or "Application"))
            return [];
        if (!IsValidHostname(hostname)) return [];

        return await Task.Run(() =>
        {
            var entries = new List<WmiEventLogEntry>();
            var scope = Connect(hostname);

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
