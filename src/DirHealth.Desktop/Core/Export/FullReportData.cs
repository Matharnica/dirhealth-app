using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.Export;

public record FullReportData(
    string Domain,
    int Score,
    List<AdFinding> Findings,
    List<AdUser> InactiveUsers,
    List<AdUser> ExpiringPasswords,
    List<string> DomainAdmins
);
