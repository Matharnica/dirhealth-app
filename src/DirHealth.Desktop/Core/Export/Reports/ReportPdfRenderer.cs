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

    private ReportPdfRenderer(ReportModel model) => _m = model;

    public static void Render(ReportModel model, string filePath)
    {
        var r = new ReportPdfRenderer(model);
        r.Draw();
        r.Finish(filePath);
    }

    // ── page flow ─────────────────────────────────────────────────────────────
    private string HeaderTitle => _m.Kind == ReportKind.Summary ? "Management Summary" : "AD Health Report";
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
            _gfx.DrawString(_section + " (continued)", PdfPageBuilder.F9B, Label, Margin, _y);
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
        _gfx.DrawString(title, PdfPageBuilder.F14B, Body, Margin, _y);
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
            _gfx.DrawString(line, font, brush, Margin + indent, _y);
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
                _gfx.DrawString(Fit(t.Headers[c].ToUpperInvariant(), PdfPageBuilder.F9B, colW - 6), PdfPageBuilder.F9B, Label, Margin + c * colW, _y);
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
                _gfx.DrawString(Fit(row[c], PdfPageBuilder.F9, colW - 6), PdfPageBuilder.F9, Body, Margin + c * colW, _y);
            _y += 14;
        }
        if (t.Rows.Count == 0) { _gfx.DrawString("None.", PdfPageBuilder.F9, Label, Margin, _y); _y += 14; }
        if (t.HiddenRows > 0) { Ensure(14); _gfx.DrawString(t.HiddenHint, PdfPageBuilder.F9B, Label, Margin, _y); _y += 14; }
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
    }

    private void DrawSummary()
    {
        _ui.DrawTitleStrip(_gfx, _page, _m.Kind == ReportKind.Summary ? "Management Summary" : "Active Directory Health Report",
            _m.Domain, _m.CreatedLocal);
        _y = PdfPageBuilder.HeaderH + 40 + 34;

        // ScoreColor is tuned for the dark cover; darkened it stays readable on white
        var scoreBrush = new XSolidBrush(Darken(_ui.ScoreColor(_m.ScoreForColour)));
        _gfx.DrawString(_m.ScoreText, PdfPageBuilder.F56B, scoreBrush, Margin, _y + 30);
        double w = _gfx.MeasureString(_m.ScoreText, PdfPageBuilder.F56B).Width;
        _gfx.DrawString($"/ 100 · {_m.ScoreLabel}", PdfPageBuilder.F14B, Label, Margin + w + 10, _y + 22);
        _gfx.DrawString(_m.ScoreDirection, PdfPageBuilder.F9, Label, Margin + w + 10, _y + 36);
        _y += 52;
        if (_m.Coverage.Length > 0) Text(_m.Coverage, PdfPageBuilder.F9, Label);
        if (_m.ScoreNote is { } note) Text(note, PdfPageBuilder.F9B, new XSolidBrush(PdfPageBuilder.ColHigh));
        if (_m.DeltaText is { } delta) Text(delta, PdfPageBuilder.F10B, Body);
        _y += 4;
        Text(string.Join("   ·   ", _m.SeverityCounts.Select(s => $"{s.Severity}: {s.Count}")), PdfPageBuilder.F10, Body);

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
        _gfx.DrawString(Fit($"{f.RuleId}  [{f.Severity}]  {f.Title}{points}", PdfPageBuilder.F10B, Width - 12), PdfPageBuilder.F10B, txt, Margin + 8, _y + 1);
        _y += 18;
        if (f.Description.Length > 0) Text(f.Description, PdfPageBuilder.F9, Label, 8);
        Text("What: " + f.What, PdfPageBuilder.F9, Body, 8);
        Text("Why: " + f.Why, PdfPageBuilder.F9, Body, 8);
        Text("How to fix: " + f.HowToFix, PdfPageBuilder.F9, Accent, 8);
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
        g.DrawString(pg, PdfPageBuilder.Mono9, brush, w - Margin - g.MeasureString(pg, PdfPageBuilder.Mono9).Width, y0 + 14);
    }

    /// <summary>Left footer text on every page (S18-2) — pure, so tests can check it without parsing the PDF.</summary>
    public static string FooterText(ReportModel m) => $"DirHealth {m.ToolVersion} · {m.Domain} · created {m.CreatedLocal}";

    /// <summary>Right footer text; drawn in the second pass once the total is known.</summary>
    public static string PageLabel(int n, int total) => $"Page {n} of {total}";

    /// <summary>Page count of a rendered report — for tests.</summary>
    public static int PageCount(string filePath) => PdfSharp.Pdf.IO.PdfReader.Open(filePath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import).PageCount;
}
