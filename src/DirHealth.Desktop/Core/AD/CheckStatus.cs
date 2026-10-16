using System.Collections.Concurrent;
using System.Diagnostics;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Storage;

namespace DirHealth.Desktop.Core.AD;

/// <summary>Outcome of one check in a scan (S16-4).</summary>
public enum CheckStatus { Found, Passed, Failed, NotApplicable }

public record CheckResult(string Category, string RuleId, CheckStatus Status, string Reason, int Count)
{
    public string StatusLabel => Status switch
    {
        CheckStatus.Found         => "Found",
        CheckStatus.Passed        => "Passed",
        CheckStatus.Failed        => "Failed",
        _                         => "Not applicable",
    };
}

/// <summary>Thrown by a check when it does not apply to this domain (e.g. no PSOs exist).</summary>
public sealed class CheckNotApplicableException(string reason) : Exception(reason);

/// <summary>
/// Runs scan sub-queries so one failure cannot take down the scan (S16-4): an exception is
/// logged and recorded per query name, the query yields an empty result, and every other
/// query runs on. Before S16 a single failing query aborted the whole scan, while other
/// checks swallowed their errors and looked "passed".
/// </summary>
public static class ScanGuard
{
    public static async Task<T> Run<T>(string name, Func<Task<T>> factory,
        ConcurrentBag<ScanLogEntry> log, ConcurrentDictionary<string, string> failures,
        ConcurrentDictionary<string, string> notApplicable)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await factory();
            sw.Stop();
            log.Add(new ScanLogEntry(name, sw.ElapsedMilliseconds, CountOf(result), null));
            return result;
        }
        catch (CheckNotApplicableException na)
        {
            sw.Stop();
            notApplicable[name] = na.Message;
            log.Add(new ScanLogEntry(name, sw.ElapsedMilliseconds, 0, $"not applicable: {na.Message}"));
            return Empty<T>();
        }
        catch (Exception ex)
        {
            sw.Stop();
            failures[name] = ex.Message;
            log.Add(new ScanLogEntry(name, sw.ElapsedMilliseconds, 0, ex.Message));
            return Empty<T>();
        }
    }

    private static T Empty<T>() =>
        typeof(T).IsValueType ? default! : (T)Activator.CreateInstance(typeof(T))!;

    private static int CountOf(object? result) => result switch
    {
        System.Collections.ICollection c => c.Count,
        int i                            => i,
        _                                => -1,
    };

    /// <summary>Category → name of the sub-query that produces it.</summary>
    public static readonly IReadOnlyDictionary<string, string> QueryOf = new Dictionary<string, string>
    {
        ["InactiveUsers"]                    = "InactiveUsers",
        ["PasswordNeverExpires"]             = "NeverExpires",
        ["ExpiredPasswords"]                 = "ExpiredPasswords",
        ["EmptyGroups"]                      = "EmptyGroups",
        ["SingleMemberGroups"]               = "SingleMemberGroups",
        ["InactiveComputers"]                = "InactiveComputers",
        ["ComputersWithoutOS"]               = "ComputersWithoutOS",
        ["KerberoastableAccounts"]           = "KerberoastableAccounts",
        ["AdminSdHolderAccounts"]            = "AdminSdHolderAccounts",
        ["WeakPasswordLength"]               = "PasswordPolicy",
        ["WeakPasswordHistory"]              = "PasswordPolicy",
        ["NoAccountLockout"]                 = "PasswordPolicy",
        ["EolOperatingSystems"]              = "EolComputers",
        ["AsRepRoasting"]                    = "AsRepRoasting",
        ["UnconstrainedDelegationComputers"] = "UnconstrDelegationComps",
        ["UnconstrainedDelegationUsers"]     = "UnconstrDelegationUsers",
        ["PasswordNotRequired"]              = "PasswordNotRequired",
        ["StaleDomainAdmins"]                = "StaleDomainAdmins",
        ["FineGrainedPasswordPolicy"]        = "FineGrainedPwdPolicies",
        ["SidHistory"]                       = "SidHistory",
        // Sprint 19
        ["HiddenPrimaryGroup"]               = "HiddenPrimaryGroup",
        ["AdminNotProtected"]                = "AdminHardening",
        ["AdminKerberoastable"]              = "AdminHardening",
        ["AdminNoPreauth"]                   = "AdminHardening",
        ["AdminOldPassword"]                 = "AdminHardening",
        ["AdminWithMailbox"]                 = "AdminHardening",
        ["OperatorGroupsNotEmpty"]           = "OperatorGroups",
        ["KrbtgtPasswordOld"]                = "DomainBasics",
        ["GuestEnabled"]                     = "DomainBasics",
        ["MachineAccountQuota"]              = "DomainBasics",
        ["PreWindows2000Access"]             = "DomainBasics",
        ["RecycleBinDisabled"]               = "DomainBasics",
        ["FunctionalLevelOld"]               = "DomainBasics",
        ["SingleDomainController"]           = "DomainBasics",
        ["DesOnly"]                          = "KerberosEncryption",
        ["ServiceAccountsWithoutAes"]        = "KerberosEncryption",
        ["ReversibleEncryption"]             = "KerberosEncryption",
        ["UserPasswordAttribute"]            = "KerberosEncryption",
        ["DelegationToDomainControllers"]    = "DelegationTrusts",
        ["DelegationToMissingAccounts"]      = "DelegationTrusts",
        ["TrustWithoutSidFiltering"]         = "DelegationTrusts",
        ["TrustTgtDelegation"]               = "DelegationTrusts",
        ["TrustInactive"]                    = "DelegationTrusts",
    };

    /// <summary>
    /// One status per catalog rule: Failed (query threw) before NotApplicable before Found
    /// (a finding exists) before Passed. A failed check never reads as passed.
    /// </summary>
    public static List<CheckResult> Evaluate(IEnumerable<FindingRule> rules, IReadOnlyList<AdFinding> findings,
        IReadOnlyDictionary<string, string> failures, IReadOnlyDictionary<string, string> notApplicable)
    {
        var results = new List<CheckResult>();
        foreach (var rule in rules)
        {
            var query = QueryOf.TryGetValue(rule.Category, out var q) ? q : rule.Category;
            var count = findings.Where(f => f.Category == rule.Category).Sum(f => Math.Max(f.Count, 1));
            results.Add(
                failures.TryGetValue(query, out var why)       ? new(rule.Category, rule.RuleId, CheckStatus.Failed, why, 0) :
                notApplicable.TryGetValue(query, out var na)   ? new(rule.Category, rule.RuleId, CheckStatus.NotApplicable, na, 0) :
                count > 0                                      ? new(rule.Category, rule.RuleId, CheckStatus.Found, "", count) :
                                                                 new(rule.Category, rule.RuleId, CheckStatus.Passed, "", 0));
        }
        return results.OrderBy(r => r.Status switch
        {
            CheckStatus.Failed => 0, CheckStatus.Found => 1, CheckStatus.NotApplicable => 2, _ => 3,
        }).ThenBy(r => r.RuleId).ToList();
    }
}
