using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Services;

namespace DirHealth.Desktop.Core.Export;

public record FullReportData(
    string Domain,
    int Score,
    List<AdFinding> Findings,
    List<AdUser> InactiveUsers,
    List<AdUser> ExpiringPasswords,
    List<string> DomainAdmins,
    int? ScoreDelta = null,   // change vs the previous scan (null when unknown)
    ScanDiff? Diff = null      // new/resolved/changed findings vs the previous scan (null when no predecessor)
);
