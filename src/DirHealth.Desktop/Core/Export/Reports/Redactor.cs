using System.Text.RegularExpressions;

namespace DirHealth.Desktop.Core.Export.Reports;

/// <summary>
/// "Redact user names" (S18-4): replaces every known account identifier — display name,
/// sAMAccountName, e-mail and DN — with a stable alias (user-001, user-002 …). Stable = the same
/// person gets the same alias everywhere in one report; aliases are numbered in sorted order so two
/// reports of the same data match. Numbers (counts, scores, dates) are left alone.
/// </summary>
public sealed class Redactor
{
    private readonly List<(string Value, string Alias)> _map;

    /// <param name="identities">One entry per person: all identifiers that name them.</param>
    public Redactor(IEnumerable<IReadOnlyCollection<string>> identities)
    {
        var people = identities
            .Select(ids => ids.Where(v => !string.IsNullOrWhiteSpace(v) && v.Length >= 2).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            .Where(ids => ids.Count > 0)
            .GroupBy(ids => ids.Min(StringComparer.OrdinalIgnoreCase)!, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.SelectMany(x => x).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            .OrderBy(ids => ids.Min(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase)
            .ToList();

        _map = [];
        for (int i = 0; i < people.Count; i++)
        {
            var alias = $"user-{i + 1:D3}";
            foreach (var v in people[i]) _map.Add((v, alias));
        }
        // longest first: a DN containing a SAM is replaced as a whole
        _map.Sort((a, b) => b.Value.Length.CompareTo(a.Value.Length));
    }

    public int Count => _map.Select(m => m.Alias).Distinct().Count();

    public string Apply(string? text)
    {
        if (string.IsNullOrEmpty(text) || _map.Count == 0) return text ?? "";
        foreach (var (value, alias) in _map)
            text = Regex.Replace(text, $@"(?<![\w.-]){Regex.Escape(value)}(?![\w-])", alias, RegexOptions.IgnoreCase);
        return text;
    }

    public ReportTable Apply(ReportTable t) =>
        t with { Rows = t.Rows.Select(r => (IReadOnlyList<string>)r.Select(Apply).ToList()).ToList() };

    public ReportModel Apply(ReportModel m) => m with
    {
        CategoryOverview = Apply(m.CategoryOverview),
        FixFirst         = Apply(m.FixFirst),
        Changes          = Apply(m.Changes),
        Findings         = m.Findings.Select(f => f with
        {
            Title = Apply(f.Title), Description = Apply(f.Description), Objects = Apply(f.Objects),
        }).ToList(),
        Exceptions   = m.Exceptions.Select(e => e with { Object = Apply(e.Object), Reason = Apply(e.Reason), CreatedBy = Apply(e.CreatedBy) }).ToList(),
        FailedChecks = m.FailedChecks.Select(c => c with { Reason = Apply(c.Reason) }).ToList(),
        Appendix     = m.Appendix.Select(a => (a.Title, Apply(a.Table))).ToList(),
        // S21: the filter text is typed by the user and may hold a name; check reasons can quote a DN.
        // References and the risk matrix are catalog text and counts — nothing personal, left as is.
        FilterScope  = m.FilterScope is null ? null : Apply(m.FilterScope),
        CheckedRules = m.CheckedRules.Select(c => c with { Reason = Apply(c.Reason) }).ToList(),
        Redacted     = true,
    };
}
