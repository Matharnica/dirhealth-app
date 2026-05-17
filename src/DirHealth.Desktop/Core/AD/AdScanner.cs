using System.DirectoryServices;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

public class AdScanner
{
    private readonly AdConnector _connector;

    private static readonly (string Name, string Risk)[] PrivilegedGroupDefs =
    [
        ("Domain Admins",       "Full domain control"),
        ("Enterprise Admins",   "Full forest control"),
        ("Schema Admins",       "Can permanently alter AD schema"),
        ("Backup Operators",    "Can read all files including NTDS.dit"),
        ("Account Operators",   "Can manage accounts and groups"),
        ("Server Operators",    "Can log on to and manage DCs"),
        ("Print Operators",     "Can load driver code on DCs"),
        ("DnsAdmins",           "Can execute DLL code on DCs via DNS plugin"),
        ("Remote Desktop Users","Can RDP into all DCs"),
    ];

    private static readonly (string Substring, DateTime EolDate)[] EolTable =
    [
        ("Windows XP",       new DateTime(2014,  4,  8)),
        ("Windows Vista",    new DateTime(2017,  4, 11)),
        ("Windows 8.1",      new DateTime(2023,  1, 10)),
        ("Windows 8",        new DateTime(2016,  1, 12)),
        ("Windows 7",        new DateTime(2020,  1, 14)),
        ("Server 2003",      new DateTime(2015,  7, 14)),
        ("Server 2008 R2",   new DateTime(2020,  1, 14)),
        ("Server 2008",      new DateTime(2020,  1, 14)),
        ("Server 2012 R2",   new DateTime(2023, 10, 10)),
        ("Server 2012",      new DateTime(2023, 10, 10)),
    ];

    private static bool TryGetEolDate(string os, out DateTime eolDate)
    {
        eolDate = default;
        if (string.IsNullOrEmpty(os)) return false;
        foreach (var (sub, date) in EolTable)
        {
            if (os.Contains(sub, StringComparison.OrdinalIgnoreCase))
            {
                eolDate = date;
                return true;
            }
        }
        return false;
    }

    public AdScanner(AdConnector connector)
    {
        _connector = connector;
    }

    public string DomainName => _connector.Domain ?? Environment.UserDomainName;

    public record CompleteScanResult(
        List<AdFinding> Findings,
        int             Score,
        List<AdUser>    InactiveUsers,
        List<AdUser>    ExpiringPasswords,
        List<string>    DomainAdmins);

    public async Task<CompleteScanResult> RunCompleteScanAsync()
    {
        var totalUsersTask     = Task.Run(() => CountObjects("(&(objectClass=user)(objectCategory=person)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))"));
        var totalGroupsTask    = Task.Run(() => CountObjects("(objectClass=group)"));
        var totalComputersTask = Task.Run(() => CountObjects("(&(objectClass=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))"));
        var inactiveUsersTask  = GetInactiveUsersAsync();
        var neverExpiresTask   = GetNeverExpiresUsersAsync();
        var expiredPwdTask     = GetExpiredPasswordUsersAsync();
        var emptyGroupsTask    = GetEmptyGroupsAsync();
        var singleMemberTask   = GetSingleMemberGroupsAsync();
        var inactiveCompsTask  = GetInactiveComputersAsync();
        var noOsTask           = GetComputersWithoutOsAsync();
        var securityTask       = GetKerberoastableAccountsAsync();
        var adminSdTask        = GetAdminSdHolderAccountsAsync();
        var policyTask         = GetPasswordPolicyFindingsAsync();
        var eolTask            = GetEolComputersAsync();
        var asRepTask          = GetAsRepRoastableAccountsAsync();
        var undelCompsTask     = GetUnconstrainedDelegationComputersAsync();
        var undelUsersTask     = GetUnconstrainedDelegationUsersAsync();
        var passwdNotReqdTask  = GetPasswordNotRequiredAccountsAsync();
        var staleDAsTask       = GetStaleDomainAdminsAsync();
        var fgppTask           = GetFineGrainedPasswordPoliciesAsync();
        var sidHistoryTask     = GetSidHistoryAccountsAsync();
        var allUsersTask       = GetAllUsersAsync();
        var domainAdminsTask   = GetDomainAdminsAsync();

        await Task.WhenAll(
            totalUsersTask, totalGroupsTask, totalComputersTask,
            inactiveUsersTask, neverExpiresTask, expiredPwdTask,
            emptyGroupsTask, singleMemberTask, inactiveCompsTask,
            noOsTask, securityTask, adminSdTask, policyTask, eolTask,
            asRepTask, undelCompsTask, undelUsersTask, passwdNotReqdTask,
            staleDAsTask, fgppTask, sidHistoryTask,
            allUsersTask, domainAdminsTask);

        var inactiveUsers      = inactiveUsersTask.Result;
        var neverExpires       = neverExpiresTask.Result;
        var expiredPwd         = expiredPwdTask.Result;
        var emptyGroups        = emptyGroupsTask.Result;
        var singleMember       = singleMemberTask.Result;
        var inactiveComps      = inactiveCompsTask.Result;
        var noOs               = noOsTask.Result;
        var kerberoastable     = securityTask.Result;
        var adminSdHolder      = adminSdTask.Result;
        var policyFindings     = policyTask.Result;
        var eolComputers       = eolTask.Result;
        var asRepRoastable     = asRepTask.Result;
        var undelComputers     = undelCompsTask.Result;
        var undelUsers         = undelUsersTask.Result;
        var passwdNotRequired  = passwdNotReqdTask.Result;
        var staleDomainAdmins  = staleDAsTask.Result;
        var fgppFindings       = fgppTask.Result;
        var sidHistoryAccounts = sidHistoryTask.Result;
        int totalUsers         = totalUsersTask.Result;
        int totalGroups        = totalGroupsTask.Result;
        int totalComputers     = totalComputersTask.Result;

        // ── Build findings ────────────────────────────────────────────────────────
        var findings = new List<AdFinding>();

        if (inactiveUsers.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "InactiveUsers",
                Title           = $"{inactiveUsers.Count} Inactive User Account(s)",
                Description     = "User accounts with no logon activity in the last 90 days.",
                Severity        = inactiveUsers.Count > 20 ? FindingSeverity.High : FindingSeverity.Medium,
                Count           = inactiveUsers.Count,
                AffectedObjects = inactiveUsers.Select(u => u.SamAccountName).ToList()
            });

        if (neverExpires.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "PasswordNeverExpires",
                Title           = $"{neverExpires.Count} Account(s) with Password Never Expires",
                Description     = "User accounts configured with passwords that never expire.",
                Severity        = FindingSeverity.Medium,
                Count           = neverExpires.Count,
                AffectedObjects = neverExpires.Select(u => u.SamAccountName).ToList()
            });

        if (expiredPwd.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "ExpiredPasswords",
                Title           = $"{expiredPwd.Count} Account(s) with Expired/Old Password",
                Description     = "User accounts whose passwords have not been changed in over 365 days.",
                Severity        = expiredPwd.Count > 10 ? FindingSeverity.High : FindingSeverity.Medium,
                Count           = expiredPwd.Count,
                AffectedObjects = expiredPwd.Select(u => u.SamAccountName).ToList()
            });

        if (emptyGroups.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "EmptyGroups",
                Title           = $"{emptyGroups.Count} Empty Group(s)",
                Description     = "Security groups with no members.",
                Severity        = FindingSeverity.Low,
                Count           = emptyGroups.Count,
                AffectedObjects = emptyGroups.Select(g => g.Name).ToList()
            });

        if (singleMember.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "SingleMemberGroups",
                Title           = $"{singleMember.Count} Group(s) with a Single Member",
                Description     = "Security groups with only one member — may indicate unnecessary groups.",
                Severity        = FindingSeverity.Low,
                Count           = singleMember.Count,
                AffectedObjects = singleMember.Select(g => g.Name).ToList()
            });

        if (inactiveComps.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "InactiveComputers",
                Title           = $"{inactiveComps.Count} Inactive Computer Account(s)",
                Description     = "Computer accounts with no activity in the last 90 days.",
                Severity        = FindingSeverity.Low,
                Count           = inactiveComps.Count,
                AffectedObjects = inactiveComps.Select(c => c.Name).ToList()
            });

        if (noOs.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "ComputersWithoutOS",
                Title           = $"{noOs.Count} Computer(s) Without OS Information",
                Description     = "Computer accounts with no operating system attribute set.",
                Severity        = FindingSeverity.Low,
                Count           = noOs.Count,
                AffectedObjects = noOs.Select(c => c.Name).ToList()
            });

        if (kerberoastable.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "KerberoastableAccounts",
                Title           = $"{kerberoastable.Count} Kerberoastable Account(s)",
                Description     = "Enabled user accounts with a Service Principal Name (SPN) set. Their password hashes can be extracted and cracked offline.",
                Severity        = kerberoastable.Count > 3 ? FindingSeverity.High : FindingSeverity.Medium,
                Count           = kerberoastable.Count,
                AffectedObjects = kerberoastable.Select(u => u.SamAccountName).ToList()
            });

        if (adminSdHolder.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "AdminSdHolderAccounts",
                Title           = $"{adminSdHolder.Count} Account(s) Protected by AdminSDHolder",
                Description     = "These accounts have AdminCount=1, indicating current or past elevated privileges. Review to ensure all are expected and necessary.",
                Severity        = FindingSeverity.Medium,
                Count           = adminSdHolder.Count,
                AffectedObjects = adminSdHolder.Select(u => u.SamAccountName).ToList()
            });

        findings.AddRange(policyFindings);

        if (eolComputers.Count > 0)
        {
            var dcEol    = eolComputers.Where(c => c.IsDomainController).ToList();
            var sev      = dcEol.Count > 0 ? FindingSeverity.High : FindingSeverity.Medium;
            var affected = eolComputers
                .OrderByDescending(c => c.IsDomainController)
                .Select(c => c.IsDomainController
                    ? $"[DC] {c.Name} — {c.OperatingSystem}"
                    : $"{c.Name} — {c.OperatingSystem}")
                .ToList();
            findings.Add(new AdFinding
            {
                Category        = "EolOperatingSystems",
                Title           = $"{eolComputers.Count} Computer(s) Running End-of-Life OS",
                Description     = "These computers run Windows versions that no longer receive security updates. Any new vulnerability remains permanently unpatched." +
                                  (dcEol.Count > 0 ? $" {dcEol.Count} domain controller(s) are affected — critical risk." : ""),
                Severity        = sev,
                Count           = eolComputers.Count,
                AffectedObjects = affected,
            });
        }

        if (asRepRoastable.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "AsRepRoasting",
                Title           = $"{asRepRoastable.Count} Account(s) Vulnerable to AS-REP Roasting",
                Description     = "These accounts have Kerberos Pre-Authentication disabled (DONT_REQUIRE_PREAUTH). An attacker can request an encrypted AS-REP ticket without any credentials and crack it offline.",
                Severity        = FindingSeverity.High,
                Count           = asRepRoastable.Count,
                AffectedObjects = asRepRoastable.Select(u => u.SamAccountName).ToList()
            });

        if (undelComputers.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "UnconstrainedDelegationComputers",
                Title           = $"{undelComputers.Count} Computer(s) with Unconstrained Delegation",
                Description     = "These non-DC computers are trusted for unconstrained Kerberos delegation. An attacker who compromises one can capture TGTs of any user authenticating to it — including Domain Admins.",
                Severity        = FindingSeverity.Critical,
                Count           = undelComputers.Count,
                AffectedObjects = undelComputers.Select(c => c.Name).ToList()
            });

        if (undelUsers.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "UnconstrainedDelegationUsers",
                Title           = $"{undelUsers.Count} User Account(s) with Unconstrained Delegation",
                Description     = "These user accounts are trusted for unconstrained Kerberos delegation. They can impersonate any user against any service in the domain.",
                Severity        = FindingSeverity.High,
                Count           = undelUsers.Count,
                AffectedObjects = undelUsers.Select(u => u.SamAccountName).ToList()
            });

        if (passwdNotRequired.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "PasswordNotRequired",
                Title           = $"{passwdNotRequired.Count} Account(s) with PASSWD_NOTREQD Flag",
                Description     = "These accounts have the PASSWD_NOTREQD flag set, allowing an empty password. Active Directory will not enforce a password for these accounts.",
                Severity        = FindingSeverity.High,
                Count           = passwdNotRequired.Count,
                AffectedObjects = passwdNotRequired.Select(u => u.SamAccountName).ToList()
            });

        if (staleDomainAdmins.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "StaleDomainAdmins",
                Title           = $"{staleDomainAdmins.Count} Stale Domain Admin Account(s)",
                Description     = "Domain Admin accounts with no logon activity in the last 30 days. Dormant admin accounts are rarely monitored and passwords are rarely rotated.",
                Severity        = FindingSeverity.High,
                Count           = staleDomainAdmins.Count,
                AffectedObjects = staleDomainAdmins.Select(u => u.SamAccountName).ToList()
            });

        findings.AddRange(fgppFindings);

        if (sidHistoryAccounts.Count > 0)
            findings.Add(new AdFinding
            {
                Category        = "SidHistory",
                Title           = $"{sidHistoryAccounts.Count} Account(s) with SID History",
                Description     = "These accounts retain SIDs from previous AD migrations. Windows honours historical SIDs silently during access checks — a migrated account may carry elevated privileges from its old domain without appearing in any privileged group.",
                Severity        = sidHistoryAccounts.Count > 5 ? FindingSeverity.High : FindingSeverity.Medium,
                Count           = sidHistoryAccounts.Count,
                AffectedObjects = sidHistoryAccounts.Select(u => u.SamAccountName).ToList()
            });

        // ── Compute score ─────────────────────────────────────────────────────────
        int score = 100;
        score -= PctPenalty(inactiveUsers.Count,    totalUsers,     maxPenalty: 20, fullAtPct: 30);
        score -= PctPenalty(neverExpires.Count,      totalUsers,     maxPenalty: 15, fullAtPct: 50);
        score -= PctPenalty(expiredPwd.Count,        totalUsers,     maxPenalty: 18, fullAtPct: 30);
        score -= PctPenalty(emptyGroups.Count,       totalGroups,    maxPenalty:  8, fullAtPct: 35);
        score -= PctPenalty(singleMember.Count,      totalGroups,    maxPenalty:  6, fullAtPct: 35);
        score -= PctPenalty(inactiveComps.Count,     totalComputers, maxPenalty: 10, fullAtPct: 40);
        score -= Math.Min(5, noOs.Count);
        score -= Math.Min(12, kerberoastable.Count * 4);
        foreach (var f in policyFindings)
            score -= f.Severity == FindingSeverity.High ? 8 : 4;
        var eolDcCount = eolComputers.Count(c => c.IsDomainController);
        var eolPcCount = eolComputers.Count(c => !c.IsDomainController);
        score -= Math.Min(15, eolDcCount * 8 + eolPcCount * 3);
        score -= Math.Min(15, asRepRoastable.Count * 3);
        score -= Math.Min(20, (undelComputers.Count + undelUsers.Count) * 6);
        score -= Math.Min(10, passwdNotRequired.Count * 2);
        score -= Math.Min(20, staleDomainAdmins.Count * 5);
        foreach (var f in fgppFindings)
            score -= f.Severity == FindingSeverity.High ? 8 : 4;
        score -= Math.Min(12, sidHistoryAccounts.Count * 3);
        score = Math.Max(10, score);

        // ── Expiring passwords (from already-loaded allUsers) ─────────────────────
        var expiringPasswords = allUsersTask.Result
            .Where(u => u.DaysUntilPasswordExpiry is >= 0 and { } d && d <= 30)
            .OrderBy(u => u.DaysUntilPasswordExpiry)
            .ToList();

        var domainAdmins = domainAdminsTask.Result?.Members?.Select(m => m.Name).ToList() ?? [];

        return new CompleteScanResult(findings, score, inactiveUsers, expiringPasswords, domainAdmins);
    }

    public async Task<List<AdUser>> GetInactiveUsersAsync(int daysThreshold = 90)
    {
        return await Task.Run(() =>
        {
            var cutoff = DateTime.UtcNow.AddDays(-daysThreshold);
            var cutoffFileTime = cutoff.ToFileTimeUtc();
            var filter = $"(&(objectClass=user)(objectCategory=person)(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         $"(|(lastLogonTimestamp<={cutoffFileTime})(!(lastLogonTimestamp=*))))";
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdUser>> GetNeverExpiresUsersAsync()
    {
        return await Task.Run(() =>
        {
            // userAccountControl bit 65536 = DONT_EXPIRE_PASSWORD
            var filter = "(&(objectClass=user)(objectCategory=person)(!(userAccountControl:1.2.840.113556.1.4.803:=2))(userAccountControl:1.2.840.113556.1.4.803:=65536))";
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdUser>> GetExpiredPasswordUsersAsync(int daysThreshold = 365)
    {
        return await Task.Run(() =>
        {
            var cutoff = DateTime.UtcNow.AddDays(-daysThreshold);
            var cutoffFileTime = cutoff.ToFileTimeUtc();
            var filter = $"(&(objectClass=user)(objectCategory=person)(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         $"(pwdLastSet<={cutoffFileTime})(pwdLastSet>=1))";
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdGroup>> GetEmptyGroupsAsync()
    {
        return await Task.Run(() =>
        {
            var filter = "(&(objectClass=group)(!(member=*)))";
            return QueryGroups(filter);
        });
    }

    public async Task<List<AdGroup>> GetSingleMemberGroupsAsync()
    {
        return await Task.Run(() =>
        {
            var groups = QueryGroups("(objectClass=group)");
            return groups.Where(g => g.MemberCount == 1).ToList();
        });
    }

    public async Task<List<AdComputer>> GetInactiveComputersAsync(int daysThreshold = 90)
    {
        return await Task.Run(() =>
        {
            var cutoff = DateTime.UtcNow.AddDays(-daysThreshold);
            var cutoffFileTime = cutoff.ToFileTimeUtc();
            var filter = $"(&(objectClass=computer)(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         $"(|(lastLogonTimestamp<={cutoffFileTime})(!(lastLogonTimestamp=*))))";
            return QueryComputers(filter);
        });
    }

    public async Task<List<AdComputer>> GetEolComputersAsync()
    {
        return await Task.Run(() =>
        {
            var computers = QueryComputers("(&(objectClass=computer)(operatingSystem=*))");
            var eol = new List<AdComputer>();
            foreach (var c in computers)
            {
                if (TryGetEolDate(c.OperatingSystem, out var eolDate))
                {
                    c.IsEol   = true;
                    c.EolDate = eolDate;
                    eol.Add(c);
                }
            }
            return eol;
        });
    }

    public async Task<List<AdDomainController>> GetAllDomainControllersAsync()
    {
        return await Task.Run(() =>
        {
            string domainDn = "", configDn = "";
            try
            {
                var dsePath = string.IsNullOrEmpty(_connector.Domain)
                    ? "LDAP://RootDSE"
                    : $"LDAP://{_connector.Domain}/RootDSE";
                using var dse = _connector.GetEntry(dsePath);
                domainDn = dse.Properties["defaultNamingContext"]?[0]?.ToString() ?? "";
                configDn = dse.Properties["configurationNamingContext"]?[0]?.ToString() ?? "";
            }
            catch { }

            var fsmo = domainDn.Length > 0 && configDn.Length > 0
                ? GetFsmoOwners(domainDn, configDn)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var gcs = configDn.Length > 0
                ? GetGlobalCatalogServers(configDn)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var dcs = new List<AdDomainController>();
            using var root     = _connector.GetRootEntry();
            using var searcher = _connector.CreateSearcher(root,
                "(&(objectClass=computer)(userAccountControl:1.2.840.113556.1.4.803:=8192))",
                "cn", "operatingSystem", "operatingSystemVersion",
                "lastLogonTimestamp", "distinguishedName");
            using var results = searcher.FindAll();

            foreach (SearchResult r in results)
            {
                var props = r.Properties;
                var name  = GetString(props, "cn");
                var os    = GetString(props, "operatingSystem");

                var dc = new AdDomainController
                {
                    Name              = name,
                    OperatingSystem   = os,
                    OsVersion         = GetString(props, "operatingSystemVersion"),
                    LastLogon         = GetDateTime(props, "lastLogonTimestamp"),
                    DistinguishedName = GetString(props, "distinguishedName"),
                    IsGlobalCatalog   = gcs.Contains(name),
                    FsmoRoles         = fsmo
                        .Where(kv => kv.Value.Equals(name, StringComparison.OrdinalIgnoreCase))
                        .Select(kv => kv.Key)
                        .ToList(),
                };

                if (TryGetEolDate(os, out var eolDate))
                {
                    dc.IsEol   = true;
                    dc.EolDate = eolDate;
                }

                dcs.Add(dc);
            }

            return dcs.OrderByDescending(d => d.IsEol).ThenBy(d => d.Name).ToList();
        });
    }

    private Dictionary<string, string> GetFsmoOwners(string domainDn, string configDn)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Read(string dn, string roleName)
        {
            try
            {
                using var entry = GetEntryByDn(dn);
                var owner = entry.Properties["fSMORoleOwner"]?[0]?.ToString() ?? "";
                if (owner.Length > 0) result[roleName] = ExtractServerFromNtdsDn(owner);
            }
            catch { }
        }

        Read(domainDn,                                    "PDC Emulator");
        Read($"CN=RID Manager$,CN=System,{domainDn}",     "RID Master");
        Read($"CN=Infrastructure,{domainDn}",             "Infrastructure Master");
        Read($"CN=Schema,{configDn}",                     "Schema Master");
        Read($"CN=Partitions,{configDn}",                 "Domain Naming Master");

        return result;
    }

    private HashSet<string> GetGlobalCatalogServers(string configDn)
    {
        var gcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var configEntry = GetEntryByDn(configDn);
            using var searcher    = _connector.CreateSearcher(configEntry,
                "(&(objectClass=nTDSDSA)(options:1.2.840.113556.1.4.803:=1))",
                "distinguishedName");
            using var results = searcher.FindAll();
            foreach (SearchResult r in results)
            {
                var dn   = r.Properties["distinguishedName"]?[0]?.ToString() ?? "";
                var name = ExtractServerFromNtdsDn(dn);
                if (name.Length > 0) gcs.Add(name);
            }
        }
        catch { }
        return gcs;
    }

    private static string ExtractServerFromNtdsDn(string ntdsDn)
    {
        // "CN=NTDS Settings,CN=<ServerName>,CN=Servers,..."
        var parts = ntdsDn.Split(',');
        if (parts.Length < 2) return ntdsDn;
        var part = parts[1].Trim();
        return part.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ? part[3..] : part;
    }

    private DirectoryEntry GetEntryByDn(string dn)
    {
        var path = string.IsNullOrEmpty(_connector.Domain)
            ? $"LDAP://{dn}"
            : $"LDAP://{_connector.Domain}/{dn}";
        return _connector.GetEntry(path);
    }

    public async Task<List<AdComputer>> GetComputersWithoutOsAsync()
    {
        return await Task.Run(() =>
        {
            var filter = "(&(objectClass=computer)(!(operatingSystem=*)))";
            return QueryComputers(filter);
        });
    }

    public async Task<int> ComputeComplianceScoreAsync()
    {
        var r = await RunCompleteScanAsync();
        return r.Score;
    }

    // Returns penalty proportional to (count/total) up to maxPenalty at fullAtPct%
    private static int PctPenalty(int count, int total, int maxPenalty, double fullAtPct)
    {
        if (total == 0 || count == 0) return 0;
        var pct = (double)count / total * 100.0;
        return (int)Math.Min(maxPenalty, Math.Round(maxPenalty * pct / fullAtPct));
    }

    private int CountObjects(string filter)
    {
        try
        {
            using var root     = _connector.GetRootEntry();
            using var searcher = _connector.CreateSearcher(root, filter, "cn");
            searcher.SearchScope = System.DirectoryServices.SearchScope.Subtree;
            using var results  = searcher.FindAll();
            return results.Count;
        }
        catch { return 0; }
    }

    public async Task<List<AdComputer>> GetAllComputersAsync()
    {
        return await Task.Run(() => QueryComputers("(&(objectClass=computer))"));
    }

    public int GetMaxPasswordAgeDays()
    {
        try
        {
            using var root = _connector.GetRootEntry();
            var val = root.Properties["maxPwdAge"]?[0];
            if (val is long l && l != 0 && l != long.MinValue)
                return (int)TimeSpan.FromTicks(-l).TotalDays;
        }
        catch { }
        return 90;
    }

    public async Task<List<AdUser>> GetAllUsersAsync()
    {
        var users = await Task.Run(() => QueryUsers("(&(objectClass=user)(objectCategory=person))"));
        var maxDays = GetMaxPasswordAgeDays();
        foreach (var u in users)
        {
            if (u.PasswordNeverExpires || u.PasswordLastSet is null)
            {
                u.DaysUntilPasswordExpiry = null;
            }
            else
            {
                var expiry = u.PasswordLastSet.Value.AddDays(maxDays);
                u.DaysUntilPasswordExpiry = (int)(expiry - DateTime.UtcNow).TotalDays;
            }
        }
        return users;
    }

    public async Task<List<string>> GetUserGroupsAsync(string distinguishedName)
    {
        return await Task.Run(() =>
        {
            var groups = new List<string>();
            try
            {
                var escaped = EscapeDn(distinguishedName);
                using var root    = _connector.GetRootEntry();
                using var searcher = _connector.CreateSearcher(root,
                    $"(&(objectClass=group)(member={escaped}))", "cn");
                using var results = searcher.FindAll();
                foreach (SearchResult r in results)
                    groups.Add(GetString(r.Properties, "cn"));
            }
            catch { }
            return groups;
        });
    }

    private static string EscapeDn(string dn) =>
        dn.Replace("\\", "\\5c").Replace("(", "\\28").Replace(")", "\\29");

    private static string EscapeFilterValue(string value) =>
        value.Replace("\\", "\\5c").Replace("*", "\\2a")
             .Replace("(", "\\28").Replace(")", "\\29").Replace("\0", "\\00");

    public async Task<List<AdUser>> GetStaleDomainAdminsAsync(int daysThreshold = 30)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var root = _connector.GetRootEntry();
                using var gs   = _connector.CreateSearcher(root,
                    "(&(objectClass=group)(cn=Domain Admins))", "distinguishedName");
                var gr = gs.FindOne();
                if (gr is null) return [];
                var daDn = GetString(gr.Properties, "distinguishedName");
                if (string.IsNullOrEmpty(daDn)) return [];

                var cutoffFileTime = DateTime.UtcNow.AddDays(-daysThreshold).ToFileTimeUtc();
                var escaped        = EscapeDn(daDn);
                // 1.2.840.113556.1.4.1941 = LDAP_MATCHING_RULE_IN_CHAIN — AD resolves nested membership server-side
                var filter         = $"(&(objectClass=user)(objectCategory=person)" +
                                     $"(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                                     $"(memberOf:1.2.840.113556.1.4.1941:={escaped})" +
                                     $"(|(lastLogonTimestamp<={cutoffFileTime})(!(lastLogonTimestamp=*))))";
                return QueryUsers(filter);
            }
            catch { return []; }
        });
    }

    public async Task<List<AdFinding>> GetFineGrainedPasswordPoliciesAsync()
    {
        return await Task.Run(() =>
        {
            var findings = new List<AdFinding>();
            try
            {
                var dsePath = string.IsNullOrEmpty(_connector.Domain)
                    ? "LDAP://RootDSE"
                    : $"LDAP://{_connector.Domain}/RootDSE";
                using var dse  = _connector.GetEntry(dsePath);
                var domainDn   = dse.Properties["defaultNamingContext"]?[0]?.ToString() ?? "";
                if (string.IsNullOrEmpty(domainDn)) return findings;

                var psoPath = string.IsNullOrEmpty(_connector.Domain)
                    ? $"LDAP://CN=Password Settings Container,CN=System,{domainDn}"
                    : $"LDAP://{_connector.Domain}/CN=Password Settings Container,CN=System,{domainDn}";

                using var psoEntry = _connector.GetEntry(psoPath);
                using var searcher = new DirectorySearcher(psoEntry)
                {
                    Filter      = "(objectClass=msDS-PasswordSettings)",
                    PageSize    = 1000,
                    SearchScope = SearchScope.OneLevel,
                };
                searcher.PropertiesToLoad.AddRange(new[]
                {
                    "name", "msDS-MinimumPasswordLength", "msDS-LockoutThreshold",
                    "msDS-PasswordReversibleEncryptionEnabled",
                });
                using var results = searcher.FindAll();

                foreach (SearchResult pso in results)
                {
                    var props      = pso.Properties;
                    var name       = GetString(props, "name");
                    var minLen     = GetPsoInt(props, "msDS-MinimumPasswordLength");
                    var lockout    = GetPsoInt(props, "msDS-LockoutThreshold");
                    var reversible = props["msDS-PasswordReversibleEncryptionEnabled"].Count > 0
                                     && props["msDS-PasswordReversibleEncryptionEnabled"][0] is true;

                    if (reversible)
                        findings.Add(new AdFinding
                        {
                            Category        = "FineGrainedPasswordPolicy",
                            Title           = $"PSO '{name}': Reversible Encryption Enabled",
                            Description     = "This Password Settings Object stores passwords using reversible encryption — equivalent to plaintext storage.",
                            Severity        = FindingSeverity.High,
                            Count           = 1,
                            AffectedObjects = [name],
                        });

                    if (minLen >= 0 && minLen < 8)
                        findings.Add(new AdFinding
                        {
                            Category        = "FineGrainedPasswordPolicy",
                            Title           = $"PSO '{name}': Weak Minimum Password Length ({minLen} chars)",
                            Description     = "This PSO requires fewer than 8 characters — weaker than the domain default policy.",
                            Severity        = FindingSeverity.High,
                            Count           = 1,
                            AffectedObjects = [name],
                        });
                    else if (minLen >= 8 && minLen < 12)
                        findings.Add(new AdFinding
                        {
                            Category        = "FineGrainedPasswordPolicy",
                            Title           = $"PSO '{name}': Low Minimum Password Length ({minLen} chars)",
                            Description     = "This PSO requires fewer than 12 characters.",
                            Severity        = FindingSeverity.Medium,
                            Count           = 1,
                            AffectedObjects = [name],
                        });

                    if (lockout == 0)
                        findings.Add(new AdFinding
                        {
                            Category        = "FineGrainedPasswordPolicy",
                            Title           = $"PSO '{name}': No Account Lockout Configured",
                            Description     = "This PSO does not configure account lockout, leaving accounts vulnerable to brute-force attacks.",
                            Severity        = FindingSeverity.High,
                            Count           = 1,
                            AffectedObjects = [name],
                        });
                }
            }
            catch { }
            return findings;
        });
    }

    private PrivilegedGroupSummary QueryPrivilegedGroup(string groupName, string risk)
    {
        var summary = new PrivilegedGroupSummary { Name = groupName, RiskDescription = risk };
        try
        {
            using var root = _connector.GetRootEntry();
            using var gs   = _connector.CreateSearcher(root,
                $"(&(objectClass=group)(cn={EscapeFilterValue(groupName)}))", "distinguishedName");
            var gr = gs.FindOne();
            if (gr is not null)
            {
                var dn      = GetString(gr.Properties, "distinguishedName");
                var escaped = EscapeDn(dn);
                var members = new List<string>();
                int start   = 0;
                while (true)
                {
                    using var rs = _connector.CreateSearcher(root,
                        $"(distinguishedName={escaped})", $"member;range={start}-*");
                    var rr = rs.FindOne();
                    if (rr is null) break;
                    bool lastPage = false;
                    int  count    = 0;
                    foreach (string key in rr.Properties.PropertyNames)
                    {
                        if (!key.StartsWith("member;range=", StringComparison.OrdinalIgnoreCase)) continue;
                        lastPage = key.EndsWith("-*", StringComparison.OrdinalIgnoreCase);
                        foreach (var v in rr.Properties[key])
                        {
                            var memberDn = v?.ToString() ?? "";
                            var cn       = memberDn.Split(',')[0];
                            if (cn.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) cn = cn[3..];
                            members.Add(cn);
                            count++;
                        }
                        break;
                    }
                    if (lastPage || count == 0) break;
                    start += count;
                }
                summary.MemberCount = members.Count;
                summary.Members     = members;
            }
        }
        catch { }
        return summary;
    }

    public async Task<List<PrivilegedGroupSummary>> GetPrivilegedGroupSummariesAsync()
    {
        var tasks = PrivilegedGroupDefs
            .Select(def => Task.Run(() => QueryPrivilegedGroup(def.Name, def.Risk)))
            .ToList();
        return [.. await Task.WhenAll(tasks)];
    }

    public async Task<List<AdUser>> GetAsRepRoastableAccountsAsync()
    {
        return await Task.Run(() =>
        {
            // userAccountControl bit 4194304 = DONT_REQUIRE_PREAUTH
            var filter = "(&(objectClass=user)(objectCategory=person)" +
                         "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         "(userAccountControl:1.2.840.113556.1.4.803:=4194304))";
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdComputer>> GetUnconstrainedDelegationComputersAsync()
    {
        return await Task.Run(() =>
        {
            // bit 524288 = TRUSTED_FOR_DELEGATION; exclude DCs (bit 8192)
            var filter = "(&(objectClass=computer)" +
                         "(!(userAccountControl:1.2.840.113556.1.4.803:=8192))" +
                         "(userAccountControl:1.2.840.113556.1.4.803:=524288))";
            return QueryComputers(filter);
        });
    }

    public async Task<List<AdUser>> GetUnconstrainedDelegationUsersAsync()
    {
        return await Task.Run(() =>
        {
            // bit 524288 = TRUSTED_FOR_DELEGATION
            var filter = "(&(objectClass=user)(objectCategory=person)" +
                         "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         "(userAccountControl:1.2.840.113556.1.4.803:=524288))";
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdUser>> GetPasswordNotRequiredAccountsAsync()
    {
        return await Task.Run(() =>
        {
            // userAccountControl bit 32 = PASSWD_NOTREQD
            var filter = "(&(objectClass=user)(objectCategory=person)" +
                         "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         "(userAccountControl:1.2.840.113556.1.4.803:=32))";
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdUser>> GetKerberoastableAccountsAsync()
    {
        return await Task.Run(() =>
        {
            var filter = "(&(objectClass=user)(objectCategory=person)" +
                         "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         "(servicePrincipalName=*)(!(samAccountName=krbtgt)))";
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdUser>> GetAdminSdHolderAccountsAsync()
    {
        return await Task.Run(() =>
        {
            var filter = "(&(objectClass=user)(objectCategory=person)" +
                         "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                         "(adminCount=1))";
            return QueryUsers(filter);
        });
    }

    public async Task<List<AdFinding>> GetPasswordPolicyFindingsAsync()
    {
        return await Task.Run(() =>
        {
            var findings = new List<AdFinding>();
            try
            {
                using var root = _connector.GetRootEntry();

                int minPwdLength      = GetRootInt(root, "minPwdLength");
                int pwdHistoryLength  = GetRootInt(root, "pwdHistoryLength");
                int lockoutThreshold  = GetRootInt(root, "lockoutThreshold");

                if (minPwdLength < 8)
                    findings.Add(new AdFinding
                    {
                        Category        = "WeakPasswordLength",
                        Title           = $"Weak Minimum Password Length ({minPwdLength} characters)",
                        Description     = "Domain policy requires fewer than 8 characters. Recommended minimum is 12.",
                        Severity        = FindingSeverity.High,
                        Count           = 1,
                        AffectedObjects = [$"minPwdLength = {minPwdLength}"]
                    });
                else if (minPwdLength < 12)
                    findings.Add(new AdFinding
                    {
                        Category        = "WeakPasswordLength",
                        Title           = $"Low Minimum Password Length ({minPwdLength} characters)",
                        Description     = "Domain policy requires fewer than 12 characters. Recommended minimum is 12.",
                        Severity        = FindingSeverity.Medium,
                        Count           = 1,
                        AffectedObjects = [$"minPwdLength = {minPwdLength}"]
                    });

                if (pwdHistoryLength < 5)
                    findings.Add(new AdFinding
                    {
                        Category        = "WeakPasswordHistory",
                        Title           = $"Insufficient Password History ({pwdHistoryLength} remembered)",
                        Description     = "Password history is too short, allowing frequent reuse. Recommended: 10 or more.",
                        Severity        = FindingSeverity.Medium,
                        Count           = 1,
                        AffectedObjects = [$"pwdHistoryLength = {pwdHistoryLength}"]
                    });

                if (lockoutThreshold == 0)
                    findings.Add(new AdFinding
                    {
                        Category        = "NoAccountLockout",
                        Title           = "Account Lockout Not Configured",
                        Description     = "No lockout threshold is set. Accounts are vulnerable to brute-force password attacks.",
                        Severity        = FindingSeverity.High,
                        Count           = 1,
                        AffectedObjects = ["lockoutThreshold = 0 (disabled)"]
                    });
            }
            catch { }
            return findings;
        });
    }

    private static int GetRootInt(DirectoryEntry root, string attr)
    {
        try
        {
            var val = root.Properties[attr]?[0];
            return val is int i ? i : 0;
        }
        catch { return 0; }
    }

    private static int GetPsoInt(ResultPropertyCollection props, string name)
    {
        if (props[name].Count == 0) return -1;
        var val = props[name][0];
        return val is int i ? i : val is long l ? (int)l : 0;
    }

    public async Task<List<AdDomainTrust>> GetDomainTrustsAsync()
    {
        return await Task.Run(() =>
        {
            var trusts = new List<AdDomainTrust>();
            string domainDn = "";
            try
            {
                var dsePath = string.IsNullOrEmpty(_connector.Domain)
                    ? "LDAP://RootDSE"
                    : $"LDAP://{_connector.Domain}/RootDSE";
                using var dse = _connector.GetEntry(dsePath);
                domainDn = dse.Properties["defaultNamingContext"]?[0]?.ToString() ?? "";
            }
            catch { }

            if (domainDn.Length == 0) return trusts;

            try
            {
                using var systemEntry = GetEntryByDn($"CN=System,{domainDn}");
                using var searcher = _connector.CreateSearcher(systemEntry,
                    "(objectClass=trustedDomain)",
                    "cn", "trustType", "trustDirection", "trustAttributes");
                searcher.SearchScope = SearchScope.OneLevel;
                using var results = searcher.FindAll();

                foreach (SearchResult r in results)
                {
                    var props      = r.Properties;
                    var trustType  = (int)GetLong(props, "trustType");
                    var direction  = (int)GetLong(props, "trustDirection");
                    var attributes = (int)GetLong(props, "trustAttributes");

                    trusts.Add(new AdDomainTrust
                    {
                        Name          = GetString(props, "cn"),
                        TrustType     = trustType switch
                        {
                            1 => "Downlevel (NT4)",
                            2 => "AD / Kerberos",
                            3 => "MIT Kerberos",
                            _ => $"Unknown ({trustType})"
                        },
                        Direction     = direction switch
                        {
                            1 => "Inbound",
                            2 => "Outbound",
                            3 => "Bidirectional",
                            _ => "Unknown"
                        },
                        IsForestTrust = (attributes & 8) != 0,
                    });
                }
            }
            catch { }

            return trusts.OrderBy(t => t.Name).ToList();
        });
    }

    public async Task<List<AdUser>> GetSidHistoryAccountsAsync()
    {
        return await Task.Run(() =>
            QueryUsers("(&(objectClass=user)(objectCategory=person)" +
                       "(!(userAccountControl:1.2.840.113556.1.4.803:=2))" +
                       "(sIDHistory=*))"));
    }

    public async Task<List<AdRecentChange>> GetRecentChangesAsync(int days = 30)
    {
        return await Task.Run(() =>
        {
            var changes  = new List<AdRecentChange>();
            var cutoff   = DateTime.UtcNow.AddDays(-days);
            var ldapTime = cutoff.ToString("yyyyMMddHHmmss.0") + "Z";
            var filter   = $"(|(&(objectClass=user)(objectCategory=person)(whenChanged>={ldapTime}))" +
                           $"(&(objectClass=computer)(whenChanged>={ldapTime})))";

            using var root     = _connector.GetRootEntry();
            using var searcher = _connector.CreateSearcher(root, filter,
                "cn", "objectClass", "whenCreated", "whenChanged", "distinguishedName");

            using var results = searcher.FindAll();
            foreach (SearchResult r in results)
            {
                var props  = r.Properties;
                var objClasses = props["objectClass"];
                bool isComputer = false;
                for (int i = 0; i < objClasses.Count; i++)
                    if (objClasses[i]?.ToString() == "computer") { isComputer = true; break; }

                var whenCreated = GetAdDateTime(props, "whenCreated");
                var whenChanged = GetAdDateTime(props, "whenChanged");
                if (whenChanged is null) continue;

                var isNew = whenCreated.HasValue
                    && (whenChanged.Value - whenCreated.Value).TotalMinutes < 5;

                changes.Add(new AdRecentChange
                {
                    Name              = GetString(props, "cn"),
                    ObjectType        = isComputer ? "Computer" : "User",
                    Action            = isNew ? "Created" : "Modified",
                    ChangedAt         = whenChanged.Value,
                    DistinguishedName = GetString(props, "distinguishedName"),
                });
            }

            return changes.OrderByDescending(c => c.ChangedAt).ToList();
        });
    }

    private static DateTime? GetAdDateTime(ResultPropertyCollection props, string name)
    {
        if (props[name].Count == 0) return null;
        var val = props[name][0];
        if (val is DateTime dt) return dt.ToUniversalTime();
        return null;
    }

    public async Task<List<AdFinding>> RunFullScanAsync()
    {
        var r = await RunCompleteScanAsync();
        return r.Findings;
    }

    public async Task<List<AdOU>> GetAllOUsAsync()
    {
        return await Task.Run(() =>
        {
            var ous = new List<AdOU>();
            using var root     = _connector.GetRootEntry();
            using var searcher = _connector.CreateSearcher(root,
                "(objectClass=organizationalUnit)",
                "ou", "description", "distinguishedName");
            using var results = searcher.FindAll();
            foreach (SearchResult r in results)
            {
                ous.Add(new AdOU
                {
                    Name              = GetString(r.Properties, "ou"),
                    DistinguishedName = GetString(r.Properties, "distinguishedName"),
                    Description       = GetString(r.Properties, "description"),
                });
            }
            return ous.OrderBy(o => o.Name).ToList();
        });
    }

    public async Task<(int Users, int Computers, int Groups)> GetOUCountsAsync(string distinguishedName)
    {
        return await Task.Run(() => (
            CountObjectsInOU(distinguishedName, "(&(objectClass=user)(objectCategory=person))"),
            CountObjectsInOU(distinguishedName, "(objectClass=computer)"),
            CountObjectsInOU(distinguishedName, "(objectClass=group)")
        ));
    }

    private int CountObjectsInOU(string ouDn, string filter)
    {
        try
        {
            using var entry    = _connector.GetEntry($"LDAP://{ouDn}");
            using var searcher = new DirectorySearcher(entry)
            {
                Filter      = filter,
                PageSize    = 1000,
                SearchScope = SearchScope.OneLevel
            };
            using var results = searcher.FindAll();
            return results.Count;
        }
        catch { return 0; }
    }

    public async Task<List<AdGroup>> GetAllGroupsWithCountAsync()
    {
        return await Task.Run(() => QueryGroups("(objectClass=group)"));
    }

    public async Task<AdGroupDetail> GetGroupDetailAsync(string distinguishedName)
    {
        return await Task.Run(() =>
        {
            var detail = new AdGroupDetail { DistinguishedName = distinguishedName };
            try
            {
                var escaped = EscapeDn(distinguishedName);
                using var root = _connector.GetRootEntry();

                // Read base group properties
                using var searcher = _connector.CreateSearcher(root,
                    $"(distinguishedName={escaped})",
                    "cn", "description", "groupType");
                var result = searcher.FindOne();
                if (result is null) return detail;

                detail.Name        = GetString(result.Properties, "cn");
                detail.Description = GetString(result.Properties, "description");

                var gt = GetLong(result.Properties, "groupType");
                detail.GroupScope = (gt & 0x8) != 0 ? "Universal"
                                  : (gt & 0x4) != 0 ? "Local"
                                  :                   "Global";

                // Range retrieval for member attribute (AD limits per-page)
                var memberDns = new List<string>();
                int start = 0;
                while (true)
                {
                    var rangeAttr = $"member;range={start}-*";
                    using var rs = _connector.CreateSearcher(root,
                        $"(distinguishedName={escaped})", rangeAttr);
                    var rr = rs.FindOne();
                    if (rr is null) break;

                    bool lastPage = false;
                    int count = 0;
                    foreach (string key in rr.Properties.PropertyNames)
                    {
                        if (!key.StartsWith("member;range=", StringComparison.OrdinalIgnoreCase)) continue;
                        lastPage = key.EndsWith("-*", StringComparison.OrdinalIgnoreCase);
                        foreach (var v in rr.Properties[key])
                        {
                            memberDns.Add(v?.ToString() ?? "");
                            count++;
                        }
                        break;
                    }
                    if (lastPage || count == 0) break;
                    start += count;
                }

                detail.Members.AddRange(BatchResolveGroupMembers(root, memberDns));
            }
            catch { }
            return detail;
        });
    }

    private List<AdGroupMember> BatchResolveGroupMembers(DirectoryEntry root, List<string> memberDns)
    {
        var members = new List<AdGroupMember>(memberDns.Count);
        const int batchSize = 50;
        for (int i = 0; i < memberDns.Count; i += batchSize)
        {
            var batch  = memberDns.Skip(i).Take(batchSize).ToList();
            var filter = "(|" + string.Join("", batch.Select(dn => $"(distinguishedName={EscapeDn(dn)})")) + ")";
            try
            {
                using var searcher = _connector.CreateSearcher(root, filter,
                    "cn", "sAMAccountName", "objectClass", "distinguishedName");
                using var results = searcher.FindAll();
                var found = new Dictionary<string, AdGroupMember>(StringComparer.OrdinalIgnoreCase);
                foreach (SearchResult r in results)
                {
                    var dn       = GetString(r.Properties, "distinguishedName");
                    var objClass = r.Properties["objectClass"].Cast<string>().ToList();
                    var type     = objClass.Contains("group")    ? "Group"
                                 : objClass.Contains("computer") ? "Computer"
                                 :                                 "User";
                    found[dn] = new AdGroupMember
                    {
                        Name              = GetString(r.Properties, "cn"),
                        SamAccountName    = GetString(r.Properties, "sAMAccountName"),
                        ObjectType        = type,
                        DistinguishedName = dn,
                    };
                }
                foreach (var dn in batch)
                    members.Add(found.TryGetValue(dn, out var m)
                        ? m
                        : new AdGroupMember { Name = dn, DistinguishedName = dn });
            }
            catch
            {
                foreach (var dn in batch)
                    members.Add(new AdGroupMember { Name = dn, DistinguishedName = dn });
            }
        }
        return members;
    }

    public async Task<AdGroupDetail> GetDomainAdminsAsync()
    {
        var dn = await Task.Run(() =>
        {
            try
            {
                using var root     = _connector.GetRootEntry();
                using var searcher = _connector.CreateSearcher(root,
                    "(&(objectClass=group)(cn=Domain Admins))", "distinguishedName");
                var result = searcher.FindOne();
                return result is not null ? GetString(result.Properties, "distinguishedName") : "";
            }
            catch { return ""; }
        });

        if (string.IsNullOrEmpty(dn))
            return new AdGroupDetail { Name = "Domain Admins", Description = "Group not found." };

        return await GetGroupDetailAsync(dn);
    }

    public async Task<List<AdUser>> GetExpiringPasswordUsersAsync(int withinDays = 30)
    {
        var allUsers = await GetAllUsersAsync();
        return allUsers
            .Where(u => u.DaysUntilPasswordExpiry is >= 0 and { } d && d <= withinDays)
            .OrderBy(u => u.DaysUntilPasswordExpiry)
            .ToList();
    }

    private List<AdUser> QueryUsers(string filter)
    {
        var users = new List<AdUser>();
        using var root     = _connector.GetRootEntry();
        using var searcher = _connector.CreateSearcher(root, filter,
            "sAMAccountName", "displayName", "mail", "lastLogonTimestamp",
            "pwdLastSet", "userAccountControl", "distinguishedName");

        using var results = searcher.FindAll();
        foreach (SearchResult result in results)
        {
            var props = result.Properties;
            users.Add(new AdUser
            {
                SamAccountName      = GetString(props, "sAMAccountName"),
                DisplayName         = GetString(props, "displayName"),
                Email               = GetString(props, "mail"),
                LastLogon           = GetDateTime(props, "lastLogonTimestamp"),
                PasswordLastSet     = GetDateTime(props, "pwdLastSet"),
                PasswordNeverExpires = (GetLong(props, "userAccountControl") & 65536) != 0,
                IsEnabled           = (GetLong(props, "userAccountControl") & 2) == 0,
                DistinguishedName   = GetString(props, "distinguishedName"),
            });
        }
        return users;
    }

    private List<AdGroup> QueryGroups(string filter)
    {
        var groups = new List<AdGroup>();
        using var root     = _connector.GetRootEntry();
        using var searcher = _connector.CreateSearcher(root, filter,
            "cn", "description", "member", "distinguishedName", "groupType");

        using var results = searcher.FindAll();
        foreach (SearchResult result in results)
        {
            var props = result.Properties;
            groups.Add(new AdGroup
            {
                Name              = GetString(props, "cn"),
                Description       = GetString(props, "description"),
                MemberCount       = props["member"].Count,
                DistinguishedName = GetString(props, "distinguishedName"),
            });
        }
        return groups;
    }

    private List<AdComputer> QueryComputers(string filter)
    {
        var computers = new List<AdComputer>();
        using var root     = _connector.GetRootEntry();
        using var searcher = _connector.CreateSearcher(root, filter,
            "cn", "operatingSystem", "operatingSystemVersion",
            "lastLogonTimestamp", "userAccountControl", "distinguishedName");

        using var results = searcher.FindAll();
        foreach (SearchResult result in results)
        {
            var props = result.Properties;
            var uac = GetLong(props, "userAccountControl");
            computers.Add(new AdComputer
            {
                Name                = GetString(props, "cn"),
                OperatingSystem     = GetString(props, "operatingSystem"),
                OsVersion           = GetString(props, "operatingSystemVersion"),
                LastLogon           = GetDateTime(props, "lastLogonTimestamp"),
                IsEnabled           = (uac & 2) == 0,
                IsDomainController  = (uac & 8192) != 0,
                DistinguishedName   = GetString(props, "distinguishedName"),
            });
        }
        return computers;
    }

    private static string GetString(ResultPropertyCollection props, string name)
        => props[name].Count > 0 ? props[name][0]?.ToString() ?? "" : "";

    private static DateTime? GetDateTime(ResultPropertyCollection props, string name)
    {
        if (props[name].Count == 0) return null;
        var val = props[name][0];
        if (val is long l && l > 0) return DateTime.FromFileTimeUtc(l);
        return null;
    }

    private static long GetLong(ResultPropertyCollection props, string name)
    {
        if (props[name].Count == 0) return 0;
        var val = props[name][0];
        return val is int i ? i : val is long l ? l : 0;
    }
}
