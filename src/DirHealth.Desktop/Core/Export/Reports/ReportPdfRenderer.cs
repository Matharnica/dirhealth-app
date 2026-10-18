using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace DirHealth.Desktop.Core.Export.Reports;

/// <summary>
/// Draws a <see cref="ReportModel"/> (S18-1..S18-4). Print discipline (S18-2): sections flow — no
/// forced page per section; object lists arrive capped from the builder; tables with six or more
/// columns get a landscape page; column headers are repeated after every page break; every page
/// gets a footer with domain, creation time and "Page x of y" (drawn in a second pass, when the
/// total is known). The renderer adds no data of its own — everything printed comes from the model.
/// S21: risk matrix on the summary page, references as link annotations per finding, "What was
/// checked" appendix; the findings tab renders through this class too (one PDF engine).
/// </summary>
public sealed class ReportPdfRenderer
{
    private const double Margin   = PdfPageBuilder.Margin;
    private const double FooterH  = PdfPageBuilder.FooterH;
    private const int    LandscapeFromColumns = 6;

    private readonly PdfPageBuilder _ui = new();
    private readonly PdfDocument _doc = new();
    private readonly ReportModel _m;
    private PdfPage   _page = null!;
    private XGraphics _gfx  = null!;
    private double    _y;
    private string    _section = "";

    private static readonly XSolidBrush Body  = new(PdfPageBuilder.ColBodyText);
    private static readonly XSolidBrush Label = new(PdfPageBuilder.ColLabelGray);
    private static readonly XSolidBrush Accent = new(PdfPageBuilder.ColGradEnd);

    private readonly Action<string>? _sink;

    private ReportPdfRenderer(ReportModel model, Action<string>? sink) { _m = model; _sink = sink; }

    public static void Render(ReportModel model, string filePath) => Render(model, filePath, null);

    /// <param name="textSink">Receives every text exactly as drawn (incl. footers) — lets tests check
    /// the PDF's content without parsing PDF streams (same pattern as the dossier).</param>
    internal static void Render(ReportModel model, string filePath, Action<string>? textSink)
    {
        var r = new ReportPdfRenderer(model, textSink);
        r.Draw();
        r.Finish(filePath);
    }

    private void DrawS(string text, XFont font, XBrush brush, double x, double y)
    {
        _sink?.Invoke(text);
        _gfx.DrawString(text, font, brush, x, y);
    }

    // ── page flow ─────────────────────────────────────────────────────────────
    private string HeaderTitle => _m.FilterScope is not null ? "Findings Report"
                                : _m.Kind == ReportKind.Summary ? "Management Summary" : "AD Health Report";
    private string HeaderRight => _m.Redacted ? $"{_m.Domain} · NAMES REDACTED" : _m.Domain;

    private double Width  => _page.Width.Point - 2 * Margin;
    private double Bottom => _page.Height.Point - FooterH - 16;

    private void NewPage(bool landscape = false)
    {
        _gfx?.Dispose();
        _page = _doc.AddPage();
        if (landscape) _page.Orientation = PageOrientation.Landscape;
        _gfx = XGraphics.FromPdfPage(_page);
        _ui.DrawHeader(_gfx, _page, HeaderTitle, HeaderRight);
        _y = PdfPageBuilder.HeaderH + 24;
        if (_section.Length > 0)
        {
            DrawS(_section + " (continued)", PdfPageBuilder.F9B, Label, Margin, _y);
            _y += 16;
        }
    }

    private bool Ensure(double h)
    {
        if (_y + h <= Bottom) return false;
        NewPage(_page.Orientation == PageOrientation.Landscape);
        return true;
    }

    private void Section(string title, double minSpace = 60)
    {
        _section = "";
        if (_y + minSpace > Bottom) NewPage();
        _y += 10;
        DrawS(title, PdfPageBuilder.F14B, Body, Margin, _y);
        _y += 6;
        _gfx.DrawLine(new XPen(PdfPageBuilder.ColBorder, 1), Margin, _y, Margin + Width, _y);
        _y += 16;
        _section = title;
    }

    private void Text(string text, XFont font, XBrush brush, double indent = 0, double lineH = 13)
    {
        foreach (var line in PdfPageBuilder.WrapText(_gfx, text, font, Width - indent))
        {
            Ensure(lineH);
            DrawS(line, font, brush, Margin + indent, _y);
            _y += lineH;
        }
    }

    /// <summary>Multi-line code (fix/undo): every source line wrapped on its own.</summary>
    private void Code(string code)
    {
        foreach (var raw in code.Split('\n'))
            Text(raw.Length == 0 ? " " : raw, PdfPageBuilder.Mono9, Body, indent: 14, lineH: 11);
    }

    private void Table(ReportTable t)
    {
        if (t.Headers.Count >= LandscapeFromColumns && _page.Orientation != PageOrientation.Landscape)
            NewPage(landscape: true);
        double colW = Width / Math.Max(1, t.Headers.Count);

        void HeaderRow()
        {
            for (int c = 0; c < t.Headers.Count; c++)
                DrawS(Fit(t.Headers[c].ToUpperInvariant(), PdfPageBuilder.F9B, colW - 6), PdfPageBuilder.F9B, Label, Margin + c * colW, _y);
            _y += 5;
            _gfx.DrawLine(new XPen(PdfPageBuilder.ColBorder, 1.5), Margin, _y, Margin + Width, _y);
            _y += 12;
        }

        Ensure(40);
        HeaderRow();
        foreach (var row in t.Rows)
        {
            if (Ensure(14)) HeaderRow();          // re-print column headers after the break
            for (int c = 0; c < row.Count && c < t.Headers.Count; c++)
                DrawS(Fit(row[c], PdfPageBuilder.F9, colW - 6), PdfPageBuilder.F9, Body, Margin + c * colW, _y);
            _y += 14;
        }
        if (t.Rows.Count == 0) { DrawS("None.", PdfPageBuilder.F9, Label, Margin, _y); _y += 14; }
        if (t.HiddenRows > 0) { Ensure(14); DrawS(t.HiddenHint, PdfPageBuilder.F9B, Label, Margin, _y); _y += 14; }
        _y += 6;
    }

    private string Fit(string text, XFont font, double width)
    {
        if (string.IsNullOrEmpty(text) || _gfx.MeasureString(text, font).Width <= width) return text ?? "";
        var s = text;
        while (s.Length > 1 && _gfx.MeasureString(s + "…", font).Width > width) s = s[..^1];
        return s + "…";
    }

    // ── content ───────────────────────────────────────────────────────────────
    private void Draw()
    {
        NewPage();
        DrawSummary();
        if (_m.Kind == ReportKind.Summary) return;

        Section($"Findings ({_m.Findings.Count})");
        if (_m.Findings.Count == 0) Text("No findings.", PdfPageBuilder.F10, Label);
        foreach (var f in _m.Findings) DrawFinding(f);

        Section($"Active exceptions ({_m.Exceptions.Count})");
        Table(new ReportTable(["Rule", "Object", "Reason", "Expires", "Created by"],
            _m.Exceptions.Select(e => (IReadOnlyList<string>)new[] { e.RuleId, e.Object, e.Reason, e.Expires, e.CreatedBy }).ToList(), 0, ""));

        Section($"Checks that could not run ({_m.FailedChecks.Count})");
        if (_m.FailedChecks.Count == 0) Text("All checks ran.", PdfPageBuilder.F10, Label);
        else Table(new ReportTable(["Rule", "Check", "Reason"],
            _m.FailedChecks.Select(c => (IReadOnlyList<string>)new[] { c.RuleId, c.Category, c.Reason }).ToList(), 0, ""));

        foreach (var (title, table) in _m.Appendix)
        {
            Section(title);
            Table(table);
        }

        DrawCheckedRules();
    }

    /// <summary>"What was checked" appendix (S21-3): every catalog rule with its status.</summary>
    private void DrawCheckedRules()
    {
        if (_m.CheckedRules.Count == 0) return;
        Section($"What was checked ({_m.CheckedRules.Count} rules)");
        Text(string.Join("   ·   ", _m.CheckedRules.GroupBy(c => c.Status).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}: {g.Count()}")), PdfPageBuilder.F10, Body);
        _y += 4;
        Table(new ReportTable(["Rule", "Check", "Status", "Note"],
            _m.CheckedRules.Select(c => (IReadOnlyList<string>)new[] { c.RuleId, c.Category, c.Status, c.Reason }).ToList(), 0, ""));
    }

    /// <summary>Risk matrix (S21-1): areas × severities, occupied cells tinted in the severity colour.</summary>
    private void DrawRiskMatrix()
    {
        var mx = _m.RiskMatrix;
        Section("Risk matrix — open findings by area and severity", 60 + 16 * mx.Rows.Count);
        double labelW = Width * 0.34;
        double cellW  = (Width - labelW) / (mx.Severities.Count + 1);
        DrawS("AREA", PdfPageBuilder.F9B, Label, Margin, _y);
        for (int c = 0; c < mx.Severities.Count; c++)
            DrawS(mx.Severities[c].ToUpperInvariant(), PdfPageBuilder.F9B, Label, Margin + labelW + c * cellW + 4, _y);
        DrawS("TOTAL", PdfPageBuilder.F9B, Label, Margin + labelW + mx.Severities.Count * cellW + 4, _y);
        _y += 5;
        _gfx.DrawLine(new XPen(PdfPageBuilder.ColBorder, 1.5), Margin, _y, Margin + Width, _y);
        _y += 4;
        foreach (var row in mx.Rows)
        {
            Ensure(16);
            DrawS(Fit($"{row.Label} ({row.Area})", PdfPageBuilder.F9, labelW - 6), PdfPageBuilder.F9, Body, Margin, _y + 11);
            for (int c = 0; c < row.Counts.Count && c < mx.Severities.Count; c++)
            {
                double x = Margin + labelW + c * cellW;
                if (row.Counts[c] > 0)
                {
                    var sev = Enum.TryParse<AD.Models.FindingSeverity>(mx.Severities[c], out var s) ? s : AD.Models.FindingSeverity.Low;
                    var (bg, border, txt) = _ui.SeverityStyle(sev);
                    _gfx.DrawRectangle(new XSolidBrush(bg), x + 1, _y, cellW - 2, 15);
                    _gfx.DrawRectangle(new XSolidBrush(border), x + 1, _y, 3, 15);
                    DrawS(row.Counts[c].ToString(System.Globalization.CultureInfo.InvariantCulture), PdfPageBuilder.F10B, txt, x + 8, _y + 11);
                }
                else DrawS("–", PdfPageBuilder.F9, Label, x + 8, _y + 11);
            }
            DrawS(row.Total.ToString(System.Globalization.CultureInfo.InvariantCulture), PdfPageBuilder.F9B, Body,
                Margin + labelW + mx.Severities.Count * cellW + 8, _y + 11);
            _y += 16;
        }
        _y += 6;
    }

    /// <summary>References of a finding (S21-2): label as a clickable link, the URL printed below it so
    /// a paper copy still names the source. Long URLs are broken by width (they have no spaces).</summary>
    private void DrawReferences(IReadOnlyList<ReportReference> refs)
    {
        if (refs.Count == 0) return;
        Text("References:", PdfPageBuilder.F9B, Label, 8);
        foreach (var r in refs)
        {
            Ensure(24);
            double x = Margin + 14;
            var label = Fit(r.Label, PdfPageBuilder.F9, Width - 14);
            DrawS(label, PdfPageBuilder.F9, Accent, x, _y);
            double w = _gfx.MeasureString(label, PdfPageBuilder.F9).Width;
            _gfx.DrawLine(new XPen(PdfPageBuilder.ColGradEnd, 0.5), x, _y + 1.5, x + w, _y + 1.5);
            var rect = _gfx.Transformer.WorldToDefaultPage(new XRect(x, _y - 9, w, 12));
            _page.AddWebLink(new PdfSharp.Pdf.PdfRectangle(rect), r.Url);
            _y += 12;
            foreach (var part in HardWrap(r.Url, PdfPageBuilder.Mono9, Width - 14))
            {
                Ensure(11);
                DrawS(part, PdfPageBuilder.Mono9, Label, x, _y);
                _y += 11;
            }
        }
    }

    private IEnumerable<string> HardWrap(string text, XFont font, double width)
    {
        var rest = text;
        while (rest.Length > 0)
        {
            int n = rest.Length;
            while (n > 1 && _gfx.MeasureString(rest[..n], font).Width > width) n--;
            yield return rest[..n];
            rest = rest[n..];
        }
    }

    private void DrawSummary()
    {
        _ui.DrawTitleStrip(_gfx, _page, _m.FilterScope is not null ? "Findings Report"
                                      : _m.Kind == ReportKind.Summary ? "Management Summary" : "Active Directory Health Report",
            _m.Domain, _m.CreatedLocal);
        _y = PdfPageBuilder.HeaderH + 40 + 34;

        // ScoreColor is tuned for the dark cover; darkened it stays readable on white
        var scoreBrush = new XSolidBrush(Darken(_ui.ScoreColor(_m.ScoreForColour)));
        DrawS(_m.ScoreText, PdfPageBuilder.F56B, scoreBrush, Margin, _y + 30);
        double w = _gfx.MeasureString(_m.ScoreText, PdfPageBuilder.F56B).Width;
        DrawS($"/ 100 · {_m.ScoreLabel}", PdfPageBuilder.F14B, Label, Margin + w + 10, _y + 22);
        DrawS(_m.ScoreDirection, PdfPageBuilder.F9, Label, Margin + w + 10, _y + 36);
        _y += 52;
        if (_m.Coverage.Length > 0) Text(_m.Coverage, PdfPageBuilder.F9, Label);
        if (_m.ScoreNote is { } note) Text(note, PdfPageBuilder.F9B, new XSolidBrush(PdfPageBuilder.ColHigh));
        if (_m.DeltaText is { } delta) Text(delta, PdfPageBuilder.F10B, Body);
        _y += 4;
        Text(string.Join("   ·   ", _m.SeverityCounts.Select(s => $"{s.Severity}: {s.Count}")), PdfPageBuilder.F10, Body);
        if (_m.FilterScope is { } scope)   // S21-4: the findings-tab PDF says which selection it shows
            Text("Filtered selection: " + scope, PdfPageBuilder.F9B, new XSolidBrush(PdfPageBuilder.ColHigh));

        DrawRiskMatrix();

        Section("Fix first — largest score gain", 80);
        Table(_m.FixFirst);
        Section("Overview by category", 80);
        Table(_m.CategoryOverview);
        if (_m.Changes.Rows.Count > 0)
        {
            Section("Changes since the last scan", 80);
            Table(_m.Changes);
        }
    }

    private void DrawFinding(ReportFinding f)
    {
        Ensure(70);
        var sev = Enum.TryParse<AD.Models.FindingSeverity>(f.Severity, out var s) ? s : AD.Models.FindingSeverity.Low;
        var (bg, border, txt) = _ui.SeverityStyle(sev);
        _gfx.DrawRectangle(new XSolidBrush(bg), Margin, _y - 12, Width, 18);
        _gfx.DrawRectangle(new XSolidBrush(border), Margin, _y - 12, 3, 18);
        var points = f.PointsIfFixed > 0 ? $"   +{f.PointsIfFixed} points if fixed" : "";
        DrawS(Fit($"{f.RuleId}  [{f.Severity}]  {f.Title}{points}", PdfPageBuilder.F10B, Width - 12), PdfPageBuilder.F10B, txt, Margin + 8, _y + 1);
        _y += 18;
        if (f.Description.Length > 0) Text(f.Description, PdfPageBuilder.F9, Label, 8);
        Text("What: " + f.What, PdfPageBuilder.F9, Body, 8);
        Text("Why: " + f.Why, PdfPageBuilder.F9, Body, 8);
        Text("How to fix: " + f.HowToFix, PdfPageBuilder.F9, Accent, 8);
        DrawReferences(f.References);
        if (f.Fix.Length > 0)
        {
            Text("Fix (PowerShell — review, then run yourself):", PdfPageBuilder.F9B, Label, 8);
            Code(f.Fix);
            Text("Operational risk: " + f.FixRisk, PdfPageBuilder.F9, new XSolidBrush(PdfPageBuilder.ColHigh), 8);
            Text("Undo:", PdfPageBuilder.F9B, Label, 8);
            Code(f.Revert);
        }
        _y += 4;
        Table(f.Objects);
        _y += 8;
    }

    private static XColor Darken(XColor c) => XColor.FromArgb((int)(c.R * 0.55), (int)(c.G * 0.55), (int)(c.B * 0.55));

    // ── footers with "Page x of y" (second pass) ─────────────────────────────
    private void Finish(string filePath)
    {
        _gfx?.Dispose();
        int total = _doc.PageCount;
        for (int i = 0; i < total; i++)
        {
            var page = _doc.Pages[i];
            using var g = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
            DrawFooter(g, page, i + 1, total);
        }
        _doc.Info.Title = $"DirHealth — {HeaderTitle}";
        _doc.Save(filePath);
    }

    private void DrawFooter(XGraphics g, PdfPage page, int n, int total)
    {
        double w  = page.Width.Point;
        double y0 = page.Height.Point - FooterH;
        g.DrawRectangle(new XSolidBrush(PdfPageBuilder.ColFooterBg), 0, y0, w, FooterH);
        g.DrawLine(new XPen(PdfPageBuilder.ColBorder, 0.5), 0, y0, w, y0);
        var brush = new XSolidBrush(PdfPageBuilder.ColLabelGray);
        g.DrawString(FooterText(_m), PdfPageBuilder.Mono9, brush, Margin, y0 + 14);
        var pg = PageLabel(n, total);
        _sink?.Invoke(pg);
        g.DrawString(pg, PdfPageBuilder.Mono9, brush, w - Margin - g.MeasureString(pg, PdfPageBuilder.Mono9).Width, y0 + 14);
    }

    /// <summary>Left footer text on every page (S18-2) — pure, so tests can check it without parsing the PDF.</summary>
    public static string FooterText(ReportModel m) => $"DirHealth {m.ToolVersion} · {m.Domain} · created {m.CreatedLocal}";

    /// <summary>Right footer text; drawn in the second pass once the total is known.</summary>
    public static string PageLabel(int n, int total) => $"Page {n} of {total}";

    /// <summary>Page count of a rendered report — for tests.</summary>
    public static int PageCount(string filePath) => PdfSharp.Pdf.IO.PdfReader.Open(filePath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import).PageCount;
}
