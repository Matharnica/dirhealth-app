using System.DirectoryServices;
using System.Globalization;
using DirHealth.Desktop.Core.AD.Checks;
using DirHealth.Desktop.Core.AD.Models;

namespace DirHealth.Desktop.Core.AD;

// Sprint 19 checks — read-only LDAP searches only. Evaluation logic lives in Checks/CheckLogic.cs,
// filters in CheckFilters, texts/fixes/weights in FindingCatalog. One method per scan sub-query; each
// returns its findings so ScanGuard can mark the whole group Failed if the query throws.
public partial class AdScanner
{
    private static AdFinding NewFinding(string category, string title, FindingSeverity severity, List<AffectedObject> objects, string description = "") =>
        new()
        {
            Category    = category,
            Title       = title,
            Severity    = severity,
            Count       = objects.Count,
            Objects     = objects,
            Description = description.Length > 0 ? description : FindingCatalog.Get(category)?.What ?? "",
        };

    /// <summary>Generic read of objects of any class → AffectedObject (name, DN, SID, type) plus extra values.</summary>
    private List<(AffectedObject Obj, Dictionary<string, List<string>> Extra)> QueryObjects(
        DirectoryEntry root, string filter, params string[] extraAttrs)
    {
        var list  = new List<(AffectedObject, Dictionary<string, List<string>>)>();
        var attrs = new[] { "cn", "sAMAccountName", "distinguishedName", "objectSid", "objectClass", "userAccountControl" }.Concat(extraAttrs).ToArray();
        using var searcher = _connector.CreateSearcher(root, filter, attrs);
        using var results  = searcher.FindAll();
        foreach (SearchResult r in results)
        {
            var p = r.Properties;
            bool isComputer = false, isGroup = false;
            foreach (var c in p["objectClass"]) { var s = c?.ToString(); isComputer |= s == "computer"; isGroup |= s == "group"; }
            var sam = GetString(p, "sAMAccountName");
            var obj = new AffectedObject
            {
                Name              = isComputer ? GetString(p, "cn") : sam.Length > 0 ? sam : GetString(p, "cn"),
                DistinguishedName = GetString(p, "distinguishedName"),
                Sid               = SidOf(p),
                Type              = isComputer ? AffectedObjectType.Computer : isGroup ? AffectedObjectType.Group : AffectedObjectType.User,
                IsDomainController = isComputer && UacFlags.IsDomainController((int)GetLong(p, "userAccountControl")),
            };
            var extra = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in extraAttrs)
                extra[a] = p[a].Cast<object>().Select(v => v?.ToString() ?? "").ToList();
            list.Add((obj, extra));
        }
        return list;
    }

    // ── S19-1 hidden primary group ────────────────────────────────────────────
    public async Task<List<AdFinding>> GetHiddenPrimaryGroupFindingsAsync() => await Task.Run(() =>
    {
        using var root = _connector.GetRootEntry();
        var objs = QueryObjects(root, CheckFilters.HiddenPrimaryGroupUsers, "primaryGroupID")
            .Concat(QueryObjects(root, CheckFilters.HiddenPrimaryGroupComputers, "primaryGroupID"))
            .Select(x => { x.Obj.Detail = $"primaryGroupID {x.Extra["primaryGroupID"].FirstOrDefault()}"; return x.Obj; })
            .ToList();
        return objs.Count == 0 ? new List<AdFinding>()
            : [NewFinding("HiddenPrimaryGroup", $"{objs.Count} Account(s) with a Non-Standard Primary Group", FindingSeverity.High, objs)];
    });

    // ── S19-2 admin hardening ─────────────────────────────────────────────────
    /// <summary>Enabled Tier 0 members (nested and primary group included), keyed by DN.</summary>
    private Dictionary<string, AdUser> GetTier0Members(DirectoryEntry root)
    {
        var members = new Dictionary<string, AdUser>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in WellKnownGroups.Tier0)
        {
            var g = _connector.ResolveGroup(root, def);
            if (!g.Found) continue;
            int? primaryRid = def.Source != GroupSidSource.Builtin && def.Rid > 0 ? def.Rid : null;
            var filter = "(&" + CheckFilters.EnabledUser + WellKnownGroups.MembershipClause(EscapeDn(g.DistinguishedName!), primaryRid) + ")";
            foreach (var u in QueryUsers(filter)) members.TryAdd(u.DistinguishedName, u);
        }
        return members;
    }

    private HashSet<string> MembersOf(DirectoryEntry root, WellKnownGroupDef def, string extraClause = "")
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var g = _connector.ResolveGroup(root, def);
        if (!g.Found) return set;
        using var s = _connector.CreateSearcher(root,
            $"(&{CheckFilters.EnabledUser}(memberOf:1.2.840.113556.1.4.1941:={EscapeDn(g.DistinguishedName!)}){extraClause})", "distinguishedName");
        using var res = s.FindAll();
        foreach (SearchResult r in res) set.Add(GetString(r.Properties, "distinguishedName"));
        return set;
    }

    public async Task<List<AdFinding>> GetAdminHardeningFindingsAsync() => await Task.Run(() =>
    {
        using var root = _connector.GetRootEntry();
        var admins    = GetTier0Members(root);
        var protectedUsers = MembersOf(root, WellKnownGroups.ProtectedUsers);
        // Mailbox = Exchange attributes present (only presence is queried; schema without Exchange → no matches)
        var withMailbox = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var s = _connector.CreateSearcher(root, "(&" + CheckFilters.EnabledUser + "(|(msExchMailboxGuid=*)(homeMDB=*)))", "distinguishedName"))
        using (var res = s.FindAll())
            foreach (SearchResult r in res)
            {
                var dn = GetString(r.Properties, "distinguishedName");
                if (admins.ContainsKey(dn)) withMailbox.Add(dn);
            }

        var now = DateTime.UtcNow;
        var buckets = new Dictionary<CheckLogic.AdminIssue, List<AffectedObject>>();
        foreach (var a in admins.Values)
        {
            var issues = CheckLogic.EvaluateAdmin(new CheckLogic.AdminFacts(a, protectedUsers.Contains(a.DistinguishedName), withMailbox.Contains(a.DistinguishedName)), now);
            foreach (CheckLogic.AdminIssue flag in Enum.GetValues<CheckLogic.AdminIssue>())
                if (flag != CheckLogic.AdminIssue.None && issues.HasFlag(flag))
                    (buckets.TryGetValue(flag, out var l) ? l : buckets[flag] = []).Add(AffectedObject.FromUser(a));
        }

        var findings = new List<AdFinding>();
        void Add(CheckLogic.AdminIssue flag, string category, string title, FindingSeverity sev)
        {
            if (buckets.TryGetValue(flag, out var objs) && objs.Count > 0)
                findings.Add(NewFinding(category, string.Format(CultureInfo.InvariantCulture, title, objs.Count), sev, objs));
        }
        Add(CheckLogic.AdminIssue.NotProtected,   "AdminNotProtected",    "{0} Tier 0 Account(s) Not Protected From Delegation",        FindingSeverity.High);
        Add(CheckLogic.AdminIssue.Kerberoastable, "AdminKerberoastable",  "{0} Tier 0 Account(s) With an SPN (Kerberoastable)",        FindingSeverity.Critical);
        Add(CheckLogic.AdminIssue.NoPreauth,      "AdminNoPreauth",       "{0} Tier 0 Account(s) Without Kerberos Pre-Authentication",  FindingSeverity.Critical);
        Add(CheckLogic.AdminIssue.OldPassword,    "AdminOldPassword",     "{0} Tier 0 Account(s) With an Old or Non-Expiring Password", FindingSeverity.High);
        Add(CheckLogic.AdminIssue.Mailbox,        "AdminWithMailbox",     "{0} Tier 0 Account(s) With a Mailbox",                        FindingSeverity.Medium);
        return findings;
    });

    public async Task<List<AdFinding>> GetOperatorGroupFindingsAsync() => await Task.Run(() =>
    {
        using var root = _connector.GetRootEntry();
        var objs = new List<AffectedObject>();
        foreach (var def in WellKnownGroups.OperatorGroups)
        {
            var g = _connector.ResolveGroup(root, def);
            if (!g.Found) continue;
            // Direct members of any class, with SID (exception key, ADR 0001)
            foreach (var (obj, _) in QueryObjects(root, $"(memberOf={EscapeDn(g.DistinguishedName!)})"))
            {
                obj.Detail = g.Name;
                objs.Add(obj);
            }
        }
        return objs.Count == 0 ? new List<AdFinding>()
            : [NewFinding("OperatorGroupsNotEmpty", $"{objs.Count} Member(s) in Operator Groups", FindingSeverity.High, objs)];
    });

    // ── S19-3 krbtgt and domain basics ───────────────────────────────────────
    public async Task<List<AdFinding>> GetDomainBasicsFindingsAsync() => await Task.Run(() =>
    {
        var findings = new List<AdFinding>();
        var now      = DateTime.UtcNow;
        using var root = _connector.GetRootEntry();
        var domainSid  = AdConnector.GetDomainSid(root);

        var krbtgt = QueryUsers("(&(objectClass=user)(sAMAccountName=krbtgt))").FirstOrDefault();
        if (krbtgt is not null && CheckLogic.KrbtgtPasswordTooOld(krbtgt.PasswordLastSet, now))
            findings.Add(NewFinding("KrbtgtPasswordOld", "krbtgt Password Older Than 180 Days", FindingSeverity.High,
                [new AffectedObject { Name = "krbtgt", DistinguishedName = krbtgt.DistinguishedName, Sid = krbtgt.Sid, Type = AffectedObjectType.User,
                                      Detail = $"last set {krbtgt.PasswordLastSet:yyyy-MM-dd}" }]));

        if (WellKnownGroups.IsDomainSid(domainSid))
        {
            var guest = QueryObjects(root, $"(&(objectSid={WellKnownGroups.DomainGroupSid(domainSid!, WellKnownGroups.GuestRid)}){CheckFilters.NotDisabled})");
            if (guest.Count > 0)
                findings.Add(NewFinding("GuestEnabled", "Built-in Guest Account Is Enabled", FindingSeverity.High, [guest[0].Obj]));
        }

        int maq = RequireRootInt(root, "ms-DS-MachineAccountQuota");
        if (maq > 0)
            findings.Add(NewFinding("MachineAccountQuota", $"Any User Can Join {maq} Computer(s) to the Domain", FindingSeverity.Medium,
                [new AffectedObject { Name = $"ms-DS-MachineAccountQuota = {maq}", Type = AffectedObjectType.DomainSetting }]));

        var preWin = _connector.ResolveGroup(root, WellKnownGroups.PreWindows2000);
        if (preWin.Found)
        {
            using var s = _connector.CreateSearcher(root, $"(distinguishedName={EscapeDn(preWin.DistinguishedName!)})", "member");
            var r = s.FindOne();
            var risky = r is null ? [] : r.Properties["member"].Cast<object>()
                .Select(m => CheckLogic.RiskyPreWin2000Member(m?.ToString() ?? "")).Where(n => n is not null).Distinct().ToList();
            if (risky.Count > 0)
                findings.Add(NewFinding("PreWindows2000Access", "Pre-Windows 2000 Compatible Access Includes Everyone/Authenticated Users", FindingSeverity.High,
                    risky.Select(n => new AffectedObject { Name = n!, Type = AffectedObjectType.DomainSetting, Detail = preWin.Name }).ToList()));
        }

        using (var dse = _connector.GetEntry(string.IsNullOrEmpty(_connector.Domain) ? "LDAP://RootDSE" : $"LDAP://{_connector.Domain}/RootDSE"))
        {
            var config = dse.Properties["configurationNamingContext"].Value?.ToString();
            if (!string.IsNullOrEmpty(config))
            {
                var path = $"CN=Recycle Bin Feature,CN=Optional Features,CN=Directory Service,CN=Windows NT,CN=Services,{config}";
                using var feature = _connector.GetEntry(string.IsNullOrEmpty(_connector.Domain) ? $"LDAP://{path}" : $"LDAP://{_connector.Domain}/{path}");
                // Back-link attribute: request it explicitly with a base search
                using var s = new DirectorySearcher(feature, "(objectClass=*)", ["msDS-EnabledFeatureBL"], SearchScope.Base);
                var fr = s.FindOne();
                if (fr is not null && fr.Properties["msDS-EnabledFeatureBL"].Count == 0)
                    findings.Add(NewFinding("RecycleBinDisabled", "AD Recycle Bin Is Not Enabled", FindingSeverity.Medium,
                        [new AffectedObject { Name = "Recycle Bin Feature", Type = AffectedObjectType.DomainSetting }]));
            }
        }

        int level = RequireRootInt(root, "msDS-Behavior-Version");
        if (level < CheckLogic.MinFunctionalLevel)
            findings.Add(NewFinding("FunctionalLevelOld", $"Domain Functional Level {CheckLogic.FunctionalLevelName(level)}", FindingSeverity.Medium,
                [new AffectedObject { Name = $"msDS-Behavior-Version = {level}", Type = AffectedObjectType.DomainSetting, Detail = CheckLogic.FunctionalLevelName(level) }]));

        var dcs = QueryObjects(root, CheckFilters.DomainControllers);
        if (dcs.Count < 2)
            findings.Add(NewFinding("SingleDomainController", $"Only {dcs.Count} Domain Controller(s)", FindingSeverity.High,
                dcs.Select(d => d.Obj).DefaultIfEmpty(new AffectedObject { Name = "no domain controller found", Type = AffectedObjectType.DomainSetting }).ToList()));
        return findings;
    });

    // ── S19-4 Kerberos encryption ────────────────────────────────────────────
    public async Task<List<AdFinding>> GetKerberosEncryptionFindingsAsync() => await Task.Run(() =>
    {
        var findings = new List<AdFinding>();
        using var root = _connector.GetRootEntry();

        var des = QueryObjects(root, CheckFilters.DesOnlyUsers).Concat(QueryObjects(root, CheckFilters.DesOnlyComputers)).Select(x => x.Obj).ToList();
        if (des.Count > 0) findings.Add(NewFinding("DesOnly", $"{des.Count} Account(s) Restricted to DES Encryption", FindingSeverity.High, des));

        var noAes = QueryUsers(CheckFilters.Kerberoastable).Where(u => CheckLogic.LacksAes(u.SupportedEncryptionTypes))
            .Select(u => { var o = AffectedObject.FromUser(u); o.Detail = u.SupportedEncryptionTypes is { } v ? $"enctypes 0x{v:X}" : "not set (RC4)"; return o; }).ToList();
        if (noAes.Count > 0) findings.Add(NewFinding("ServiceAccountsWithoutAes", $"{noAes.Count} Service Account(s) Without AES", FindingSeverity.Medium, noAes));

        var rev = QueryObjects(root, CheckFilters.ReversibleUsers).Concat(QueryObjects(root, CheckFilters.ReversibleComputers)).Select(x => x.Obj).ToList();
        if (rev.Count > 0) findings.Add(NewFinding("ReversibleEncryption", $"{rev.Count} Account(s) With Reversible Password Encryption", FindingSeverity.High, rev));

        // Presence filter only — the userPassword / unixUserPassword values are never requested
        var upw = QueryObjects(root, CheckFilters.UserPasswordAttribute).Select(x => x.Obj).ToList();
        if (upw.Count > 0) findings.Add(NewFinding("UserPasswordAttribute", $"{upw.Count} Account(s) With userPassword/unixUserPassword Set", FindingSeverity.High, upw));
        return findings;
    });

    // ── S19-5 delegation and trusts ──────────────────────────────────────────
    public async Task<List<AdFinding>> GetDelegationTrustFindingsAsync() => await Task.Run(() =>
    {
        var findings = new List<AdFinding>();
        using var root = _connector.GetRootEntry();

        var dcHosts = QueryObjects(root, CheckFilters.DomainControllers, "dNSHostName")
            .SelectMany(d => d.Extra["dNSHostName"].Append(d.Obj.Name)).Where(h => h.Length > 0).ToList();

        var sources = QueryObjects(root, CheckFilters.ConstrainedDelegationSources, "msDS-AllowedToDelegateTo");
        var toDc = new List<AffectedObject>();
        var missing = new List<AffectedObject>();
        var spnCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        bool SpnExists(string spn)
        {
            if (spnCache.TryGetValue(spn, out var known)) return known;
            using var s = _connector.CreateSearcher(root, $"(servicePrincipalName={EscapeFilterValue(spn)})", "cn");
            s.SizeLimit = 1;
            return spnCache[spn] = s.FindOne() is not null;
        }
        foreach (var (obj, extra) in sources)
        {
            var targets = extra["msDS-AllowedToDelegateTo"];
            var dcTargets = targets.Where(t => CheckLogic.TargetsDomainController(t, dcHosts)).ToList();
            if (dcTargets.Count > 0) toDc.Add(new AffectedObject { Name = obj.Name, DistinguishedName = obj.DistinguishedName, Sid = obj.Sid, Type = obj.Type,
                                                                   Detail = "→ " + string.Join(", ", dcTargets) });
            var gone = targets.Where(t => CheckLogic.SpnIsMissing(t, SpnExists)).ToList();
            if (gone.Count > 0) missing.Add(new AffectedObject { Name = obj.Name, DistinguishedName = obj.DistinguishedName, Sid = obj.Sid, Type = obj.Type,
                                                                 Detail = "→ " + string.Join(", ", gone) });
        }
        foreach (var (obj, _) in QueryObjects(root, CheckFilters.RbcdOnDcOrKrbtgt))
            toDc.Add(new AffectedObject { Name = obj.Name, DistinguishedName = obj.DistinguishedName, Sid = obj.Sid, Type = obj.Type,
                                          Detail = "resource-based delegation configured on this object" });
        if (toDc.Count > 0) findings.Add(NewFinding("DelegationToDomainControllers", $"{toDc.Count} Delegation Path(s) to Domain Controllers or krbtgt", FindingSeverity.Critical, toDc));
        if (missing.Count > 0) findings.Add(NewFinding("DelegationToMissingAccounts", $"{missing.Count} Account(s) Delegating to Non-Existent Services", FindingSeverity.Medium, missing));

        // Trusts: trustedDomain objects under CN=System
        var domainDn = root.Properties["distinguishedName"].Value?.ToString() ?? "";
        var now = DateTime.UtcNow;
        using var system = _connector.GetEntry(string.IsNullOrEmpty(_connector.Domain) ? $"LDAP://CN=System,{domainDn}" : $"LDAP://{_connector.Domain}/CN=System,{domainDn}");
        using var ts = new DirectorySearcher(system, CheckFilters.Trusts, ["name", "trustAttributes", "trustDirection", "whenChanged", "distinguishedName"], SearchScope.OneLevel);
        using var tr = ts.FindAll();
        var noFilter = new List<AffectedObject>(); var tgt = new List<AffectedObject>(); var inactive = new List<AffectedObject>();
        foreach (SearchResult r in tr)
        {
            var p = r.Properties;
            var facts = new CheckLogic.TrustFacts(GetString(p, "name"), (int)GetLong(p, "trustAttributes"), (int)GetLong(p, "trustDirection"),
                p["whenChanged"].Count > 0 && p["whenChanged"][0] is DateTime wc ? DateTime.SpecifyKind(wc, DateTimeKind.Utc) : null);
            var issues = CheckLogic.EvaluateTrust(facts, now);
            AffectedObject O() => new() { Name = facts.Name, DistinguishedName = GetString(p, "distinguishedName"), Type = AffectedObjectType.DomainSetting,
                                          Detail = $"trustAttributes 0x{facts.TrustAttributes:X}" };
            if (issues.HasFlag(CheckLogic.TrustIssue.NoSidFiltering)) noFilter.Add(O());
            if (issues.HasFlag(CheckLogic.TrustIssue.TgtDelegation))  tgt.Add(O());
            if (issues.HasFlag(CheckLogic.TrustIssue.Inactive))       inactive.Add(O());
        }
        if (noFilter.Count > 0) findings.Add(NewFinding("TrustWithoutSidFiltering", $"{noFilter.Count} Trust(s) Without SID Filtering", FindingSeverity.High, noFilter));
        if (tgt.Count > 0)      findings.Add(NewFinding("TrustTgtDelegation", $"{tgt.Count} Trust(s) Allowing TGT Delegation", FindingSeverity.High, tgt));
        if (inactive.Count > 0) findings.Add(NewFinding("TrustInactive", $"{inactive.Count} Inactive Trust(s)", FindingSeverity.Low, inactive));
        return findings;
    });
}
