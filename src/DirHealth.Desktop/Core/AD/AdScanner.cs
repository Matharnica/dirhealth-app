using System.DirectoryServices;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

public class AdScanner
{
    private readonly AdConnector _connector;

    public AdScanner(AdConnector connector)
    {
        _connector = connector;
    }

    public string DomainName => _connector.Domain ?? Environment.UserDomainName;

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
        var policyTask         = GetPasswordPolicyFindingsAsync();

        await Task.WhenAll(totalUsersTask, totalGroupsTask, totalComputersTask,
                           inactiveUsersTask, neverExpiresTask, expiredPwdTask,
                           emptyGroupsTask, singleMemberTask, inactiveCompsTask,
                           noOsTask, securityTask, policyTask);

        int totalUsers     = totalUsersTask.Result;
        int totalGroups    = totalGroupsTask.Result;
        int totalComputers = totalComputersTask.Result;

        int score = 100;

        // User health — percentage-based (full penalty at given threshold %)
        score -= PctPenalty(inactiveUsersTask.Result.Count, totalUsers,     maxPenalty: 20, fullAtPct: 30);
        score -= PctPenalty(neverExpiresTask.Result.Count,  totalUsers,     maxPenalty: 15, fullAtPct: 50);
        score -= PctPenalty(expiredPwdTask.Result.Count,    totalUsers,     maxPenalty: 18, fullAtPct: 30);

        // Group health — percentage-based
        score -= PctPenalty(emptyGroupsTask.Result.Count,   totalGroups,    maxPenalty:  8, fullAtPct: 35);
        score -= PctPenalty(singleMemberTask.Result.Count,  totalGroups,    maxPenalty:  6, fullAtPct: 35);

        // Computer health — percentage-based
        score -= PctPenalty(inactiveCompsTask.Result.Count, totalComputers, maxPenalty: 10, fullAtPct: 40);
        score -= Math.Min(5, noOsTask.Result.Count);

        // Security checks — absolute (critical regardless of environment size)
        score -= Math.Min(12, securityTask.Result.Count * 4);
        foreach (var f in policyTask.Result)
            score -= f.Severity == FindingSeverity.High ? 8 : 4;

        // Floor at 10 — a non-zero score shows there's always room to improve
        return Math.Max(10, score);
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

    public async Task<List<AdFinding>> RunFullScanAsync()
    {
        var findings = new List<AdFinding>();

        var inactiveUsers        = await GetInactiveUsersAsync();
        var neverExpiresUsers    = await GetNeverExpiresUsersAsync();
        var expiredPasswords     = await GetExpiredPasswordUsersAsync();
        var emptyGroups          = await GetEmptyGroupsAsync();
        var singleMemberGroups   = await GetSingleMemberGroupsAsync();
        var inactiveComputers    = await GetInactiveComputersAsync();
        var noOsComputers        = await GetComputersWithoutOsAsync();
        var kerberoastable       = await GetKerberoastableAccountsAsync();
        var adminSdHolder        = await GetAdminSdHolderAccountsAsync();
        var policyFindings       = await GetPasswordPolicyFindingsAsync();

        if (inactiveUsers.Count > 0)
            findings.Add(new AdFinding
            {
                Category = "InactiveUsers",
                Title = $"{inactiveUsers.Count} Inactive User Account(s)",
                Description = "User accounts with no logon activity in the last 90 days.",
                Severity = inactiveUsers.Count > 20 ? FindingSeverity.High : FindingSeverity.Medium,
                Count = inactiveUsers.Count,
                AffectedObjects = inactiveUsers.Select(u => u.SamAccountName).ToList()
            });

        if (neverExpiresUsers.Count > 0)
            findings.Add(new AdFinding
            {
                Category = "PasswordNeverExpires",
                Title = $"{neverExpiresUsers.Count} Account(s) with Password Never Expires",
                Description = "User accounts configured with passwords that never expire.",
                Severity = FindingSeverity.Medium,
                Count = neverExpiresUsers.Count,
                AffectedObjects = neverExpiresUsers.Select(u => u.SamAccountName).ToList()
            });

        if (expiredPasswords.Count > 0)
            findings.Add(new AdFinding
            {
                Category = "ExpiredPasswords",
                Title = $"{expiredPasswords.Count} Account(s) with Expired/Old Password",
                Description = "User accounts whose passwords have not been changed in over 365 days.",
                Severity = expiredPasswords.Count > 10 ? FindingSeverity.High : FindingSeverity.Medium,
                Count = expiredPasswords.Count,
                AffectedObjects = expiredPasswords.Select(u => u.SamAccountName).ToList()
            });

        if (emptyGroups.Count > 0)
            findings.Add(new AdFinding
            {
                Category = "EmptyGroups",
                Title = $"{emptyGroups.Count} Empty Group(s)",
                Description = "Security groups with no members.",
                Severity = FindingSeverity.Low,
                Count = emptyGroups.Count,
                AffectedObjects = emptyGroups.Select(g => g.Name).ToList()
            });

        if (singleMemberGroups.Count > 0)
            findings.Add(new AdFinding
            {
                Category = "SingleMemberGroups",
                Title = $"{singleMemberGroups.Count} Group(s) with a Single Member",
                Description = "Security groups with only one member — may indicate unnecessary groups.",
                Severity = FindingSeverity.Low,
                Count = singleMemberGroups.Count,
                AffectedObjects = singleMemberGroups.Select(g => g.Name).ToList()
            });

        if (inactiveComputers.Count > 0)
            findings.Add(new AdFinding
            {
                Category = "InactiveComputers",
                Title = $"{inactiveComputers.Count} Inactive Computer Account(s)",
                Description = "Computer accounts with no activity in the last 90 days.",
                Severity = FindingSeverity.Low,
                Count = inactiveComputers.Count,
                AffectedObjects = inactiveComputers.Select(c => c.Name).ToList()
            });

        if (noOsComputers.Count > 0)
            findings.Add(new AdFinding
            {
                Category = "ComputersWithoutOS",
                Title = $"{noOsComputers.Count} Computer(s) Without OS Information",
                Description = "Computer accounts with no operating system attribute set.",
                Severity = FindingSeverity.Low,
                Count = noOsComputers.Count,
                AffectedObjects = noOsComputers.Select(c => c.Name).ToList()
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

        return findings;
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

                foreach (var dn in memberDns)
                    detail.Members.Add(ResolveGroupMember(dn));
            }
            catch { }
            return detail;
        });
    }

    private AdGroupMember ResolveGroupMember(string memberDn)
    {
        try
        {
            using var entry = _connector.GetEntry($"LDAP://{EscapeDn(memberDn)}");
            var objClass = entry.Properties["objectClass"].Cast<string>().ToList();
            var type = objClass.Contains("group")    ? "Group"
                     : objClass.Contains("computer") ? "Computer"
                     :                                 "User";
            return new AdGroupMember
            {
                Name              = entry.Properties["cn"]?[0]?.ToString() ?? memberDn,
                SamAccountName    = entry.Properties["sAMAccountName"]?[0]?.ToString() ?? "",
                ObjectType        = type,
                DistinguishedName = memberDn,
            };
        }
        catch
        {
            return new AdGroupMember { Name = memberDn, DistinguishedName = memberDn };
        }
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
            computers.Add(new AdComputer
            {
                Name              = GetString(props, "cn"),
                OperatingSystem   = GetString(props, "operatingSystem"),
                OsVersion         = GetString(props, "operatingSystemVersion"),
                LastLogon         = GetDateTime(props, "lastLogonTimestamp"),
                IsEnabled         = (GetLong(props, "userAccountControl") & 2) == 0,
                DistinguishedName = GetString(props, "distinguishedName"),
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
