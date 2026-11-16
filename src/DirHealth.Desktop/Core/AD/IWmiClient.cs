using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

/// <summary>
/// Live per-computer details over ICMP + WMI (fix H1, 2026-11-16). Callers pass the computer's
/// FQDN from dNSHostName — never the cn. Invalid hosts return empty/-1 without any network access;
/// connection and query failures throw (translate with <see cref="WmiErrors.Describe"/>).
/// </summary>
public interface IWmiClient
{
    Task<long> PingTimeMs(string fqdn);
    Task<List<WmiDisk>> GetDisksAsync(string fqdn);
    Task<List<WmiLocalAdmin>> GetLocalAdminsAsync(string fqdn);
    Task<List<WmiLoggedOnUser>> GetLoggedOnUsersAsync(string fqdn);
    Task<List<WmiEventLogEntry>> GetEventLogAsync(string fqdn, string logName, string? severityFilter, int maxEntries);
}
