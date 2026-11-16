namespace DirHealth.Desktop.Core.Export.Reports;

/// <summary>One printed line of a wrapped code block (F-01).</summary>
/// <param name="Text">The text exactly as printed.</param>
/// <param name="IsContinuation">True when the line continues the source line above it.</param>
public record CodeLine(string Text, bool IsContinuation);

/// <summary>
/// Line breaking for the PDF report (Paket 3, F-01/F-02). Pure functions — the caller passes the text
/// width measure, so tests run without a PDF or font.
/// </summary>
public static class TextWrap
{
    /// <summary>Marker that starts a printed line which is NOT a valid PowerShell continuation (F-01).
    /// Deliberately not PowerShell syntax: a pasted line that starts with it fails loudly ("not recognized")
    /// instead of running a fragment with a changed meaning.</summary>
    public const string HardBreakMarker = "» ";

    /// <summary>Prefix of a wrapped comment line: it stays a comment, so it can never run as a command.</summary>
    public const string CommentContinuation = "# » ";

    /// <summary>Extra indent of a continuation line (after a backtick, pipe or comma).</summary>
    public const string ContinuationIndent = "    ";

    // ── prose and table cells (F-02) ──────────────────────────────────────────

    /// <summary>
    /// Greedy word wrap. Words wider than <paramref name="width"/> (URLs, DNs, SIDs) are broken by
    /// width instead of running past the margin. Existing line breaks are kept. Never returns an empty list.
    /// </summary>
    public static IReadOnlyList<string> Words(string? text, double width, Func<string, double> measure)
    {
        var lines = new List<string>();
        foreach (var para in (text ?? "").Replace("\r\n", "\n").Split('\n'))
        {
            var current = "";
            foreach (var word in para.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (measure(candidate) <= width) { current = candidate; continue; }
                if (current.Length > 0) lines.Add(current);
                current = "";
                var rest = word;
                while (measure(rest) > width && rest.Length > 1)
                {
                    int n = Fit(rest, width, measure);
                    lines.Add(rest[..n]);
                    rest = rest[n..];
                }
                current = rest;
            }
            lines.Add(current);
        }
        // drop trailing empty lines produced by a trailing newline, keep at least one line
        while (lines.Count > 1 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    /// <summary>Longest prefix (at least one character) of <paramref name="s"/> that fits <paramref name="width"/>.</summary>
    private static int Fit(string s, double width, Func<string, double> measure)
    {
        int lo = 1, hi = s.Length;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) / 2;
            if (measure(s[..mid]) <= width) lo = mid; else hi = mid - 1;
        }
        return lo;
    }

    // ── PowerShell code (F-01) ────────────────────────────────────────────────

    /// <summary>
    /// Wraps a PowerShell script for print at <paramref name="maxCols"/> monospace columns so that a reader
    /// who copies it from the PDF never runs something else than the source:
    /// <list type="bullet">
    /// <item>Indentation is kept (tabs become four spaces).</item>
    /// <item>Code is broken, by preference, after <c>|</c> or <c>,</c> outside quotes (PowerShell continues
    /// the statement on the next line by itself) or at a blank outside quotes with a trailing backtick
    /// (PowerShell's own line continuation). Copied, these lines run exactly like the source line.</item>
    /// <item>A quoted string longer than the line (LDAP filters, regexes) is split into parts joined by <c>+</c>
    /// inside parentheses — <c>("(&amp;(objectClass=user)…" +</c> / <c>"…(pwdLastSet&gt;=1))")</c> — which PowerShell
    /// evaluates to the very same string, in expression and in argument position. Never inside <c>$name</c>,
    /// <c>$(…)</c>, an escape (<c>`x</c>, <c>""</c>, <c>''</c>).</item>
    /// <item>A comment is continued with <c># » </c> — still a comment, so a wrapped comment can never run as a command.</item>
    /// <item>Only when none of this applies (a bare token longer than the line, a here-string) the line is broken
    /// hard and the next line starts with <c>» </c>, which is not PowerShell: pasting it fails at that line.</item>
    /// </list>
    /// </summary>
    public static IReadOnlyList<CodeLine> Code(string? code, int maxCols)
    {
        maxCols = Math.Max(20, maxCols);
        var result = new List<CodeLine>();
        foreach (var raw in (code ?? "").Replace("\r\n", "\n").Split('\n'))
            WrapCodeLine(raw.Replace("\t", "    ").TrimEnd(), maxCols, result);
        while (result.Count > 1 && result[^1].Text.Length == 0) result.RemoveAt(result.Count - 1);
        return result;
    }

    private static void WrapCodeLine(string line, int maxCols, List<CodeLine> output)
    {
        if (line.Length <= maxCols) { output.Add(new CodeLine(line, false)); return; }

        int indentLen = line.Length - line.TrimStart(' ').Length;
        string indent = line[..indentLen];
        int commentAt = CommentStart(line);

        // 1) code part (before a trailing comment)
        string code    = commentAt >= 0 ? line[..commentAt].TrimEnd() : line;
        string comment = commentAt >= 0 ? line[commentAt..] : "";
        bool first = true;

        if (code.Trim().Length > 0)
        {
            // a comment that fits behind the code stays there
            if (comment.Length > 0 && code.Length + 1 + comment.Length <= maxCols)
            {
                output.Add(new CodeLine(code + " " + comment, false));
                return;
            }
            WrapCodePart(code, indent, maxCols, output);
            first = false;
        }
        if (comment.Length == 0) return;

        // 2) comment part: own line(s), every continuation again a comment
        var body = comment.TrimStart('#').TrimStart();
        string head = indent + "# ";
        string cont = indent + CommentContinuation;
        var current = head;
        bool currentIsCont = !first;
        foreach (var word in body.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var prefixOnly = current == head || current == cont;
            var candidate = prefixOnly ? current + word : current + " " + word;
            if (candidate.Length <= maxCols) { current = candidate; continue; }
            if (!prefixOnly) { output.Add(new CodeLine(current, currentIsCont)); current = cont; currentIsCont = true; }
            var rest = word;
            while (current.Length + rest.Length > maxCols)
            {
                int take = Math.Max(1, maxCols - current.Length);
                output.Add(new CodeLine(current + rest[..take], currentIsCont));
                rest = rest[take..];
                current = cont; currentIsCont = true;
            }
            current += rest;
        }
        if (current != head && current != cont) output.Add(new CodeLine(current, currentIsCont));
    }

    private static void WrapCodePart(string code, string indent, int maxCols, List<CodeLine> output)
    {
        var rest = code;
        bool cont = false;
        // very deep indents would leave no room for text — continuation lines then start at column 0
        string contPrefix = indent.Length + ContinuationIndent.Length + HardBreakMarker.Length <= maxCols / 2
            ? indent + ContinuationIndent : "";
        while (true)
        {
            if (rest.Length <= maxCols) { output.Add(new CodeLine(rest, cont)); return; }
            int at = SafeBreak(rest, maxCols, out bool needsBacktick);
            if (at > 0)
            {
                var head = rest[..at].TrimEnd();
                output.Add(new CodeLine(needsBacktick ? head + " `" : head, cont));
                rest = contPrefix + rest[at..].TrimStart();
                cont = true;
                continue;
            }
            // A long string literal: split it into ("part" + / "part") — the same value, copy-safe.
            if (SplitString(rest, maxCols, contPrefix, output, cont) is { } after)
            {
                rest = after;
                cont = true;
                continue;
            }
            // No safe point: hard break, every further line marked as not-PowerShell. Once broken hard, the
            // quote state of the rest is unknown, so the rest is only broken hard as well (at a blank if possible).
            bool first = true;
            while (rest.Length > maxCols)
            {
                int minKeep = first ? 1 : contPrefix.Length + HardBreakMarker.Length + 1;
                int take = HardBreakAt(rest, maxCols, minKeep);
                output.Add(new CodeLine(rest[..take], cont));
                rest = contPrefix + HardBreakMarker + rest[take..];
                cont = true; first = false;
            }
            output.Add(new CodeLine(rest, true));
            return;
        }
    }

    /// <summary>
    /// Splits the quoted string that crosses column <paramref name="maxCols"/> into <c>("a" +</c> … <c>"z")</c>.
    /// Writes all lines but the last to <paramref name="output"/> and returns the last line (the final part plus
    /// whatever followed the string), or null when there is no such string or no room.
    /// </summary>
    internal static string? SplitString(string rest, int maxCols, string contPrefix, List<CodeLine> output, bool cont)
    {
        if (FindLongString(rest, maxCols) is not { } span) return null;
        var (q, e) = span;
        char quote = rest[q];
        string content = rest[(q + 1)..e];
        var ok = SplitPoints(content, quote);

        // the largest allowed split ≤ room; at least 4 characters per part, or the split is not worth it
        int Take(int from, int room)
        {
            for (int k = Math.Min(content.Length - 1, from + room); k >= from + 4; k--)
                if (ok[k]) return k;
            return -1;
        }

        var lines = new List<string>();
        int pos = Take(0, maxCols - q - 5);                 // head + "(" + quote + part + quote + " +"
        if (pos < 0) return null;
        lines.Add(rest[..q] + "(" + quote + content[..pos] + quote + " +");
        while (true)
        {
            int roomLast = maxCols - contPrefix.Length - 3;  // quote + part + quote + ")"
            if (content.Length - pos <= roomLast) break;
            int next = Take(pos, maxCols - contPrefix.Length - 4);
            if (next < 0) return null;
            lines.Add(contPrefix + quote + content[pos..next] + quote + " +");
            pos = next;
        }
        for (int i = 0; i < lines.Count; i++) output.Add(new CodeLine(lines[i], cont || i > 0));
        return contPrefix + quote + content[pos..] + quote + ")" + rest[(e + 1)..];
    }

    /// <summary>Start and end (index of the closing quote) of the string literal that crosses column
    /// <paramref name="maxCols"/>; null if the overflow is not inside a closed '…' or "…" (here-strings are left alone).</summary>
    private static (int Start, int End)? FindLongString(string s, int maxCols)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '`') { i++; continue; }
            if (c == '#' && (i == 0 || s[i - 1] == ' ')) return null;          // rest of the line is a comment
            if (c is not ('"' or '\'')) continue;
            if (i > 0 && s[i - 1] == '@') return null;                         // here-string
            int end = ClosingQuote(s, i);
            if (end < 0) return null;
            if (end >= maxCols) return i + 6 <= maxCols ? (i, end) : null;
            i = end;
        }
        return null;
    }

    private static int ClosingQuote(string s, int open)
    {
        char quote = s[open];
        for (int i = open + 1; i < s.Length; i++)
        {
            if (quote == '"' && s[i] == '`') { i++; continue; }
            if (s[i] != quote) continue;
            if (i + 1 < s.Length && s[i + 1] == quote) { i++; continue; }      // "" or '' escape
            return i;
        }
        return -1;
    }

    /// <summary>ok[k] = the content may be split before index k without changing the string's value.</summary>
    internal static bool[] SplitPoints(string content, char quote)
    {
        int n = content.Length;
        var ok = Enumerable.Repeat(true, n + 1).ToArray();
        ok[0] = ok[n] = false;
        for (int i = 0; i < n; i++)
        {
            char c = content[i];
            if (c == quote && i + 1 < n && content[i + 1] == quote) { ok[i + 1] = false; i++; continue; }   // escape pair
            if (quote != '"') continue;                                                                    // '…' is literal
            if (c == '`') { if (i + 1 <= n) ok[i + 1] = false; i++; continue; }
            if (c != '$' || i + 1 >= n) continue;
            int j = i + 1;
            if (content[j] is '(' or '{')
            {
                char open = content[j], close = open == '(' ? ')' : '}';
                int depth = 0;
                for (; j < n; j++)
                {
                    if (content[j] == open) depth++;
                    else if (content[j] == close && --depth == 0) break;
                }
            }
            else
            {
                while (j < n && (char.IsLetterOrDigit(content[j]) || content[j] is '_' or ':' or '?')) j++;
                j--;
            }
            for (int k = i + 1; k <= Math.Min(j, n - 1); k++) ok[k] = false;   // not inside $name / $(…) / ${…}
            i = Math.Max(i, j);
        }
        return ok;
    }

    /// <summary>Length of the head of a hard break: after the last blank within the line (readable), otherwise the full line.</summary>
    private static int HardBreakAt(string s, int maxCols, int minKeep)
    {
        minKeep = Math.Max(minKeep, s.Length - s.TrimStart(' ').Length + 1);   // never break inside the indentation
        int blank = s.LastIndexOf(' ', Math.Min(maxCols, s.Length) - 1);
        return blank >= minKeep + 8 ? blank + 1 : maxCols;
    }

    /// <summary>
    /// Index where the line may be split safely, or -1. Prefers the last <c>|</c> or <c>,</c> outside quotes
    /// (the text up to and including it stays on the line); otherwise the last blank outside quotes, which
    /// then needs a trailing backtick. The kept part including " `" must fit <paramref name="maxCols"/>.
    /// </summary>
    internal static int SafeBreak(string s, int maxCols, out bool needsBacktick)
    {
        needsBacktick = false;
        int minKeep = s.Length - s.TrimStart(' ').Length + 8;   // never leave a stub of a few characters
        int lastOp = -1, lastBlank = -1, brackets = 0;
        char quote = '\0';
        for (int i = 0; i < s.Length && i < maxCols; i++)
        {
            char c = s[i];
            if (quote != '\0')
            {
                if (c == '`') { i++; continue; }                     // escaped char in "…"
                if (c == quote)
                {
                    if (i + 1 < s.Length && s[i + 1] == quote) { i++; continue; }   // '' or "" escape
                    quote = '\0';
                }
                continue;
            }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == '`') { i++; continue; }
            if (c == '[') { brackets++; continue; }                  // [type[a,b]] / $a[0] — never split inside
            if (c == ']') { brackets = Math.Max(0, brackets - 1); continue; }
            if (brackets > 0) continue;
            if ((c is '|' or ',') && i + 1 >= minKeep && i + 1 <= maxCols) lastOp = i + 1;
            else if (c == ' ' && i >= minKeep && i + 2 <= maxCols && i > 0 && s[i - 1] != '`') lastBlank = i;
        }
        if (lastOp > 0) return lastOp;
        if (lastBlank > 0) { needsBacktick = true; return lastBlank; }
        return -1;
    }

    /// <summary>Index of a <c>#</c> that starts a comment (outside quotes, at line start or after a blank), or -1.</summary>
    internal static int CommentStart(string s)
    {
        char quote = '\0';
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (quote != '\0')
            {
                if (c == '`') { i++; continue; }
                if (c == quote) quote = '\0';
                continue;
            }
            if (c is '"' or '\'') { quote = c; continue; }
            if (c == '`') { i++; continue; }
            if (c == '#' && (i == 0 || s[i - 1] is ' ' or '\t')) return i;
        }
        return -1;
    }

    // ── table columns (F-02) ──────────────────────────────────────────────────

    /// <summary>
    /// Column widths that add up to <paramref name="total"/>. With <paramref name="weights"/> (one per column)
    /// the widths are proportional to them. Without, they follow the content: a column whose widest cell
    /// (<paramref name="natural"/>) fits its fair share gets exactly that, the remaining width is shared by
    /// the wide columns ("water filling"); spare width is spread proportionally. No column gets less than
    /// <paramref name="minWidth"/> unless the table is too narrow for that.
    /// </summary>
    public static double[] ColumnWidths(IReadOnlyList<double> natural, double total, IReadOnlyList<double>? weights = null, double minWidth = 36)
    {
        int n = natural.Count;
        if (n == 0) return [];
        if (weights is { } w && w.Count == n && w.All(x => x > 0))
        {
            double sum = w.Sum();
            return w.Select(x => total * x / sum).ToArray();
        }
        double min = Math.Min(minWidth, total / n);
        var want = natural.Select(x => Math.Max(min, x)).ToArray();
        var result = new double[n];
        var open = Enumerable.Range(0, n).ToList();
        double remaining = total;
        while (open.Count > 0)
        {
            double share = remaining / open.Count;
            var fits = open.Where(c => want[c] <= share).ToList();
            if (fits.Count == 0)
            {
                foreach (var c in open) result[c] = share;
                return result;
            }
            foreach (var c in fits) { result[c] = want[c]; remaining -= want[c]; open.Remove(c); }
        }
        // everything fits: spread the spare width proportionally
        double used = result.Sum();
        return used <= 0 ? result : result.Select(x => x * total / used).ToArray();
    }
}
