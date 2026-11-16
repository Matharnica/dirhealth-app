using System.Text.RegularExpressions;
using DirHealth.Desktop.Core.AD;

namespace DirHealth.Desktop.Core.Export.Reports;

/// <summary>What an alias stands for — the placeholder names the object type (Paket 3, F-03).</summary>
public enum RedactKind
{
    /// <summary>A person: user account, exception creator → "user-001".</summary>
    User,
    /// <summary>An account of unknown type (e.g. a Domain Admins member known only by name) → "account-001".</summary>
    Account,
    Computer,
    Group,
    Policy,
}

/// <summary>
/// "Redact user names" (S18-4, reworked in Paket 3 F-03/F-04): replaces every known account identifier —
/// display name, sAMAccountName, e-mail, DN, "DOMAIN\sam" — with a stable, typed alias (user-001,
/// account-001 …). Stable = the same account gets the same alias everywhere in one report (identifiers
/// that share a value are merged); aliases are numbered per type in sorted order, so two reports of the
/// same data match. Numbers (counts, scores, dates) are left alone.
/// <para>
/// Matching: a field that IS an identifier is replaced whole (any length). Inside free text only whole
/// tokens are replaced, and only identifiers of at least <see cref="MinFreeTextLength"/> characters, so a
/// short account name ("it") cannot garble an English sentence. Catalog and scanner texts (titles,
/// descriptions, What/Why/How) are never passed through the redactor — they name no person.
/// </para>
/// Built-in accounts and groups (krbtgt, Guest, Administrator, Domain Admins …) are not people and are
/// never collected — see <see cref="IsWellKnown"/>.
/// </summary>
public sealed class Redactor
{
    /// <summary>Shortest identifier that is replaced inside free text (whole fields are always replaced).</summary>
    public const int MinFreeTextLength = 3;

    /// <summary>What a redacted "Created by" shows when its value was not a known identifier.</summary>
    public const string RedactedPlaceholder = "(redacted)";

    private readonly Dictionary<string, string> _exact = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Regex Pattern, string Alias)> _tokens = [];

    /// <param name="identities">One entry per person: all identifiers that name them (all treated as users).</param>
    public Redactor(IEnumerable<IReadOnlyCollection<string>> identities)
        : this(identities.Select(ids => (RedactKind.User, ids))) { }

    /// <param name="identities">One entry per object: its type and all identifiers that name it.</param>
    public Redactor(IEnumerable<(RedactKind Kind, IReadOnlyCollection<string> Ids)> identities)
    {
        // merge entries that share an identifier: one object, one alias
        var groups = new List<Identity>();
        foreach (var (kind, ids) in identities)
        {
            var clean = new HashSet<string>(ids.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()), StringComparer.OrdinalIgnoreCase);
            if (clean.Count == 0) continue;
            var merged = new Identity(kind, clean);
            foreach (var hit in groups.Where(g => g.Ids.Overlaps(clean)).ToList())
            {
                merged.Ids.UnionWith(hit.Ids);
                merged.Kind = (RedactKind)Math.Min((int)merged.Kind, (int)hit.Kind);   // User wins: safer to call a person a person
                groups.Remove(hit);
            }
            groups.Add(merged);
        }

        var all = new List<(string Value, string Alias)>();
        foreach (var byKind in groups.GroupBy(g => g.Kind))
        {
            int n = 0;
            foreach (var g in byKind.OrderBy(g => g.Ids.Min(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase))
            {
                var alias = $"{Prefix(byKind.Key)}-{++n:D3}";
                foreach (var v in g.Ids) all.Add((v, alias));
            }
        }
        foreach (var (v, a) in all) _exact[v] = a;
        // longest first: a DN containing a SAM is replaced as a whole
        foreach (var (v, a) in all.Where(x => x.Value.Length >= MinFreeTextLength).OrderByDescending(x => x.Value.Length))
            _tokens.Add((new Regex($@"(?<![\w.\-]){Regex.Escape(v)}(?![\w\-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), a));
        Count = all.Select(x => x.Alias).Distinct().Count();
    }

    private sealed class Identity(RedactKind kind, HashSet<string> ids)
    {
        public RedactKind Kind { get; set; } = kind;
        public HashSet<string> Ids { get; } = ids;
    }

    public static string Prefix(RedactKind k) => k switch
    {
        RedactKind.User     => "user",
        RedactKind.Computer => "computer",
        RedactKind.Group    => "group",
        RedactKind.Policy   => "policy",
        _                   => "account",
    };

    /// <summary>Number of distinct aliases.</summary>
    public int Count { get; }

    /// <summary>A whole field that is an identifier becomes its alias; otherwise known names inside the text
    /// are replaced as whole tokens.</summary>
    public string Apply(string? text)
    {
        if (string.IsNullOrEmpty(text) || _exact.Count == 0) return text ?? "";
        if (_exact.TryGetValue(text.Trim(), out var whole)) return whole;
        foreach (var (pattern, alias) in _tokens)
            text = pattern.Replace(text, alias);
        return text;
    }

    public ReportTable Apply(ReportTable t) =>
        t with { Rows = t.Rows.Select(r => (IReadOnlyList<string>)r.Select(Apply).ToList()).ToList() };

    /// <summary>
    /// Redacts every field of the model that can hold an account name. Left as is on purpose: titles,
    /// descriptions, the category overview, "Fix first", changes and the summary sentences — scanner and
    /// catalog texts that name no person (redacting them garbled sentences, F-03); references, risk matrix
    /// and counts.
    /// </summary>
    public ReportModel Apply(ReportModel m) => m with
    {
        Findings     = m.Findings.Select(f => f with { Objects = Apply(f.Objects) }).ToList(),
        Exceptions   = m.Exceptions.Select(e => e with
        {
            Object    = Apply(e.Object),
            Reason    = Apply(e.Reason),
            CreatedBy = RedactAlways(e.CreatedBy),    // F-04: an admin's own account is personal data
        }).ToList(),
        FailedChecks = m.FailedChecks.Select(c => c with { Reason = Apply(c.Reason) }).ToList(),
        FailedChecksShort = m.FailedChecksShort.Select(Apply).ToList(),
        Appendix     = m.Appendix.Select(a => (a.Title, Apply(a.Table))).ToList(),
        // S21: the filter text is typed by the user and may hold a name; check reasons can quote a DN.
        FilterScope  = m.FilterScope is null ? null : Apply(m.FilterScope),
        CheckedRules = m.CheckedRules.Select(c => c with { Reason = Apply(c.Reason) }).ToList(),
        Redacted     = true,
        RedactionNote = ReportBuilder.RedactionNoteText,
    };

    /// <summary>Like <see cref="Apply(string?)"/>, but a non-empty value that is not a known identifier
    /// becomes <see cref="RedactedPlaceholder"/> — for fields that always name a person.</summary>
    public string RedactAlways(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? "";
        var r = Apply(value);
        return string.Equals(r, value, StringComparison.Ordinal) ? RedactedPlaceholder : r;
    }

    // ── built-in accounts and groups (F-03) ──────────────────────────────────

    /// <summary>Relative IDs below 1000 are reserved for built-in accounts and groups (500 Administrator,
    /// 501 Guest, 502 krbtgt, 512 Domain Admins …); user-created objects start at 1000.</summary>
    public const int FirstUserRid = 1000;

    /// <summary>Default names of built-in accounts — used only when no SID is known (name-only lists).</summary>
    private static readonly HashSet<string> WellKnownAccountNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "krbtgt", "Guest", "Gast", "Invité", "Administrator", "Administrateur", "Administrador", "Administratör",
        "DefaultAccount", "WDAGUtilityAccount",
        "Everyone", "Authenticated Users", "Domain Users", "Domain Computers", "Domain Controllers",
        "Domänen-Admins", "Organisations-Admins", "Schema-Admins", "Administratoren",
    };

    /// <summary>
    /// True for built-in accounts and groups, which are not people and stay readable in a redacted report.
    /// By SID first: any SID outside the domain space (S-1-5-32-…, S-1-1-0 …) and domain SIDs with a
    /// RID below <see cref="FirstUserRid"/>. Without a SID by the default names and the names of
    /// <see cref="WellKnownGroups"/>.
    /// </summary>
    public static bool IsWellKnown(string? sid, string? name)
    {
        if (!string.IsNullOrWhiteSpace(sid))
        {
            var s = sid.Trim();
            if (!s.StartsWith("S-", StringComparison.OrdinalIgnoreCase)) return IsWellKnownName(name);
            if (!s.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)) return true;
            var rid = s[(s.LastIndexOf('-') + 1)..];
            return int.TryParse(rid, out var r) && r < FirstUserRid;
        }
        return IsWellKnownName(name);
    }

    public static bool IsWellKnownName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        var n = name.Trim();
        return WellKnownAccountNames.Contains(n)
            || WellKnownGroups.Privileged.Concat(WellKnownGroups.Tier0).Append(WellKnownGroups.ProtectedUsers).Append(WellKnownGroups.PreWindows2000)
                .Any(g => string.Equals(g.FallbackName, n, StringComparison.OrdinalIgnoreCase));
    }
}
