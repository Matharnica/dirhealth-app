using System.Globalization;
using DirHealth.Desktop.Core.AD.Models;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using UI = DirHealth.Desktop.Core.Export.PdfPageBuilder;

namespace DirHealth.Desktop.Core.Export.Reports;

/// <summary>
/// Draws a <see cref="ReportModel"/> (S18-1..S18-4) as a security-assessment report: a cover (score ring,
/// severity tiles, scan facts, classification), an executive summary (summary sentences, key facts,
/// severity distribution, score trend, failed checks, "Fix first", risk matrix, scope, overview), one card
/// per finding (Full) and an appendix (exceptions, failed checks, user lists, "What was checked", how the
/// score works). Print discipline (S18-2): sections flow; headings stay with their first content; tables
/// repeat their column headers after a break; tables with six or more columns get a landscape page;
/// every page has the footer with "Page x of y" (second pass). The renderer adds no data of its own —
/// every value printed comes from the model; only fixed labels ("Why it matters") are its own.
/// Paket 3: table cells wrap instead of being cut (F-02); PowerShell is wrapped so a copied line never
/// runs something else (F-01).
/// </summary>
public sealed class ReportPdfRenderer
{
    private readonly ReportModel _m;
    private readonly PdfFlow _f;
    private bool _wrapNoteShown;
    private readonly List<(string Label, int Page)> _toc = [];   // contents on the cover, drawn once page numbers are known
    private double _tocTop, _tocBottom;

    private static XSolidBrush Ink   => PdfFlow.Ink;
    private static XSolidBrush Ink2  => PdfFlow.Ink2;
    private static XSolidBrush Muted => PdfFlow.Muted;
    private static readonly XSolidBrush OnDark      = new(UI.ColOnDark);
    private static readonly XSolidBrush OnDarkMuted = new(UI.ColOnDarkMuted);
    private static readonly XSolidBrush White       = new(XColors.White);

    private ReportPdfRenderer(ReportModel model, Action<string>? sink)
    {
        _m = model;
        _f = new PdfFlow(HeaderTitle, HeaderRight, sink);
    }

    public static void Render(ReportModel model, string filePath) => Render(model, filePath, null);

    /// <param name="textSink">Receives every text exactly as drawn (incl. footers) — lets tests check
    /// the PDF's content without parsing PDF streams (same pattern as the dossier).</param>
    internal static void Render(ReportModel model, string filePath, Action<string>? textSink)
    {
        var r = new ReportPdfRenderer(model, textSink);
        r.Draw();
        r._f.Finish(filePath, $"DirHealth — {r.HeaderTitle}", FooterText(model), r.DrawContents);
    }

    private string HeaderTitle => _m.FilterScope is not null ? "Findings Report"
                                : _m.Kind == ReportKind.Summary ? "Management Summary" : "AD Health Report";
    private string HeaderRight => string.Join(" · ", new[] { _m.Domain, _m.Redacted ? "NAMES REDACTED" : "", _m.Classification }
                                                     .Where(s => !string.IsNullOrEmpty(s)));
    private string CoverTitle => _m.CoverTitle.Length > 0 ? _m.CoverTitle : HeaderTitle;

    private void T(string text, XFont font, XBrush brush, double x, double baseline) => _f.Text(text, font, brush, x, baseline);
    private double W(string s, XFont font) => _f.W(s, font);
    private XGraphics G => _f.G;
    private static double M => PdfFlow.Margin;

    // ── content ───────────────────────────────────────────────────────────────
    private void Draw()
    {
        DrawCover();
        _f.NewPage();
        DrawExecutiveSummary();
        if (_m.Kind == ReportKind.Summary) return;

        _f.Continued = "";
        _f.NewPage();
        _f.Heading($"Detailed findings ({_m.Findings.Count})");
        Mark("Detailed findings");
        if (_m.Findings.Count == 0) _f.Paragraph("No findings.", UI.F9, Muted);
        foreach (var f in _m.Findings) DrawFinding(f);

        _f.Continued = "";
        _f.NewPage();
        DrawAppendix();
    }

    /// <summary>Records the page of a heading just drawn for the contents on the cover.</summary>
    private void Mark(string label) => _toc.Add((label, _f.Doc.PageCount));

    /// <summary>
    /// Contents on the cover (second pass — the page numbers are known only now). Two columns between the
    /// cover notes and the read-only statement; left out when that space is too small.
    /// </summary>
    private void DrawContents()
    {
        if (_toc.Count == 0) return;
        var entries = _toc.Take(8).ToList();
        int rows = (entries.Count + 1) / 2;
        const double rowH = 14;
        double need = 20 + rows * rowH;
        if (_tocTop + need > _tocBottom) return;
        var page = _f.Doc.Pages[0];
        using var g = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
        double y = _tocBottom - need, w = page.Width.Point - 2 * M, colW = (w - 28) / 2;
        _f.Sink("CONTENTS");
        g.DrawString("CONTENTS", UI.F7B, Muted, M, y + 8);
        g.DrawLine(new XPen(UI.ColBorder, 0.8), M, y + 13, M + w, y + 13);
        y += 20;
        var leader = new XPen(UI.ColBorderDark, 0.6) { DashStyle = XDashStyle.Dot };
        for (int i = 0; i < entries.Count; i++)
        {
            var (label, pageNo) = entries[i];
            double x = M + (i / rows) * (colW + 28), ly = y + (i % rows) * rowH + 10;
            var no = pageNo.ToString(CultureInfo.InvariantCulture);
            _f.Sink(label); _f.Sink(no);
            g.DrawString(label, UI.F8, Ink, x, ly);
            double lw = g.MeasureString(label, UI.F8).Width, nw = g.MeasureString(no, UI.F8B).Width;
            g.DrawLine(leader, x + lw + 6, ly, x + colW - nw - 6, ly);
            g.DrawString(no, UI.F8B, Ink, x + colW - nw, ly);
        }
    }

    // ── cover ─────────────────────────────────────────────────────────────────
    /// <summary>
    /// Cover — what a reader who reads nothing else needs (V-11): title, domain, score ring with label (or a
    /// neutral "not available", F-05), delta, scan facts, open findings per severity, notes, read-only statement.
    /// </summary>
    private void DrawCover()
    {
        _f.NewPage(bare: true);
        double pw = _f.PageW, ph = _f.PageH;
        const double blockH = 432;
        G.DrawRectangle(new XSolidBrush(UI.ColNavy), 0, 0, pw, blockH);
        G.DrawRectangle(new XSolidBrush(UI.ColAccent), 0, blockH - 3, pw, 3);

        // brand line + markers
        _f.Ui.DrawLogo(G, M, 40, 24);
        T("DirHealth", UI.F12B, White, M + 32, 57);
        double px = pw - M;
        var markers = new List<(string Text, XColor Fill, XColor TextC, XColor? Outline)>();
        if (_m.Classification.Length > 0) markers.Add((_m.Classification, UI.ColNavy, UI.ColOnDark, UI.ColOnDarkMuted));
        if (_m.Redacted) markers.Add(("NAMES REDACTED", UI.ColHigh, XColors.White, null));
        foreach (var (text, fill, tc, outline) in markers)
        {
            double w = W(text, UI.F7B) + 12;
            px -= w;
            UI.Pill(G, text, UI.F7B, fill, tc, px, 56, outline, _f.SinkAction);
            px -= 8;
        }

        // title block (left) — leaves room for the ring on the right
        const double ringR = 66;
        double ringCx = pw - M - ringR - 8, ringCy = 208;
        double textW = ringCx - ringR - M - 28;
        double y = 140;
        T("ACTIVE DIRECTORY SECURITY ASSESSMENT", UI.F7B, OnDarkMuted, M, y);
        y += 34;
        foreach (var line in _f.Wrap(CoverTitle, UI.F26B, textW)) { T(line, UI.F26B, White, M, y); y += 32; }
        y += 2;
        foreach (var line in _f.Wrap(_m.Domain.Length > 0 ? _m.Domain : "—", UI.F15B, textW)) { T(line, UI.F15B, OnDark, M, y); y += 20; }
        if (_m.FilterScope is { } scope)   // S21-4: the findings-tab PDF says which selection it shows
        {
            y += 6;
            foreach (var line in _f.Wrap("Filtered selection: " + scope, UI.F9B, textW)) { T(line, UI.F9B, new XSolidBrush(UI.ColScoreWarn), M, y); y += 13; }
        }
        if (!_m.ScoreAvailable)
        {
            // F-05: no number, no traffic-light colour — a neutral statement
            y += 8;
            foreach (var line in _f.Wrap(_m.ScoreText, UI.F9B, textW)) { T(line, UI.F9B, OnDark, M, y); y += 13; }
        }

        // score ring (right)
        UI.Ring(G, ringCx, ringCy, ringR, 11, _m.ScoreAvailable ? Math.Clamp(_m.ScoreForColour / 100.0, 0, 1) : 0,
                UI.ColNavy3, _f.Ui.ScoreColor(_m.ScoreForColour));
        if (_m.ScoreAvailable)
        {
            var num = _m.ScoreText;
            var font = W(num, UI.F36B) > 2 * ringR - 34 ? UI.F26B : UI.F36B;
            T(num, font, White, ringCx - W(num, font) / 2, ringCy + font.Size * 0.30);
            const string of = "/ 100";
            T(of, UI.F8, OnDarkMuted, ringCx - W(of, UI.F8) / 2, ringCy + font.Size * 0.30 + 15);
            if (_m.ScoreLabel.Length > 0)
            {
                var label = _m.ScoreLabel.ToUpperInvariant();
                double lw = W(label, UI.F8B) + 12;
                UI.Pill(G, label, UI.F8B, UI.ColNavy2, _f.Ui.ScoreColor(_m.ScoreForColour), ringCx - lw / 2, ringCy + ringR + 26,
                        _f.Ui.ScoreColor(_m.ScoreForColour), _f.SinkAction);
            }
        }
        else
        {
            const string dash = "—";
            T(dash, UI.F36B, OnDarkMuted, ringCx - W(dash, UI.F36B) / 2, ringCy + 11);
        }
        double ry = ringCy + ringR + 44;
        foreach (var line in new[] { _m.ScoreDirection, _m.DeltaText ?? "" }.Where(s => s.Length > 0))
        {
            var s = _f.Fit(line, UI.F8, 2 * ringR + 50);
            T(s, UI.F8, OnDarkMuted, ringCx - W(s, UI.F8) / 2, ry);
            ry += 12;
        }

        // scan facts strip at the bottom of the dark block
        var facts = _m.Scope.Where(l => l.Label is not ("Domain" or "Checks")).Take(5).ToList();
        if (facts.Count > 0)
        {
            double stripTop = blockH - 3 - 74, cellW = (pw - 2 * M) / facts.Count;
            G.DrawRectangle(new XSolidBrush(UI.ColNavy2), 0, stripTop, pw, 74);
            for (int i = 0; i < facts.Count; i++)
            {
                double x = M + i * cellW;
                T(facts[i].Label.ToUpperInvariant(), UI.F7B, OnDarkMuted, x, stripTop + 24);
                var lines = _f.Wrap(facts[i].Value, UI.F9, cellW - 14);
                for (int k = 0; k < Math.Min(2, lines.Count); k++)
                    T(k == 1 && lines.Count > 2 ? _f.Fit(lines[k] + " …", UI.F9, cellW - 14) : lines[k], UI.F9, White, x, stripTop + 41 + k * 13);
            }
        }

        // white part: open findings by severity
        _f.Y = blockH + 30;
        T(_m.SeverityCountsLabel, UI.F7B, Muted, M, _f.Y);
        _f.Y += 10;
        double gap = 10, tileW = (_f.Width - 3 * gap) / 4, tileH = 64;
        int col = 0;
        foreach (var (sev, count) in _m.SeverityCounts.Take(4))
        {
            var (tint, accent, txt) = _f.Ui.SeverityStyle(UI.ParseSeverity(sev));
            UI.Tile(G, M + col * (tileW + gap), _f.Y, tileW, tileH, count.ToString(CultureInfo.InvariantCulture), sev,
                    accent, count > 0 ? tint : UI.ColSurface, count > 0 ? txt.Color : UI.ColLabelGray, _f.SinkAction);
            col++;
        }
        _f.Y += tileH + 12;
        foreach (var line in new[] { _m.ExceptedNote, _m.Coverage, _m.Scope.FirstOrDefault(l => l.Label == "Checks")?.Value }
                                 .Where(s => !string.IsNullOrEmpty(s)))
            _f.Paragraph(line!, UI.F8, Muted, 0, 12);
        if (_m.ScoreNote is { } note) _f.Paragraph(note, UI.F8B, _f.Ui.SeverityStyle(FindingSeverity.High).text, 0, 12);
        _f.Y += 8;
        if (_m.RedactionNote is { } rn)
            _f.Callout("This report is redacted", [rn], UI.ColHigh, _f.Ui.SeverityStyle(FindingSeverity.High).text);

        // read-only statement at the bottom of the cover; the contents (second pass) go between
        _tocTop = _f.Y + 4;
        _tocBottom = ph - PdfFlow.FooterH - 12;
        if (_m.Disclaimer.Length > 0)
        {
            var lines = _f.Wrap(_m.Disclaimer, UI.F7, _f.Width);
            double dy = Math.Max(_f.Y + 6, ph - PdfFlow.FooterH - 14 - lines.Count * 10.5);
            _tocBottom = dy - 22;
            foreach (var line in lines) { T(line, UI.F7, Muted, M, dy); dy += 10.5; }
        }
    }

    // ── executive summary ─────────────────────────────────────────────────────
    private void DrawExecutiveSummary()
    {
        _f.Heading("Executive summary", 100);
        Mark("Executive summary");
        foreach (var sentence in _m.SummarySentences) { _f.Paragraph(sentence, UI.F10, Ink, 0, 15.5); _f.Y += 4; }
        var facts = _m.KeyFacts ?? [];
        if (facts.Count > 0)
        {
            _f.Y += 4;
            foreach (var fact in facts) { _f.Bullet(fact, UI.F9, Ink2); _f.Y += 2; }
        }
        _f.Y += 10;
        DrawChartsRow();

        if (_m.FailedChecksShort.Count > 0)
            _f.Callout("Checks that could not run — the score does not cover them:", _m.FailedChecksShort,
                       UI.ColHigh, _f.Ui.SeverityStyle(FindingSeverity.High).text);

        DrawFixFirst();
        DrawRiskMatrix();

        _f.Heading("Scan scope", 80);
        if (_m.Kind == ReportKind.Summary) Mark("Scan scope");
        _f.KeyValues(_m.Scope.Select(l => (l.Label, l.Value, false))
            .Append(("Score", _m.ScoreAvailable ? $"{_m.ScoreText} / 100 · {_m.ScoreLabel}" : _m.ScoreText, false))
            .Append(("Score scale", _m.ScoreLegend, false)));

        _f.Heading("Overview by finding", PdfFlow.KeepFor(_m.CategoryOverview));
        Mark("Overview by finding");
        _f.Table(_m.CategoryOverview, [CellKind.Mono, CellKind.Text, CellKind.Severity, CellKind.Number, CellKind.Number]);
        if (_m.Changes.Rows.Count > 0)
        {
            _f.Heading("Changes since the last scan (objects)", PdfFlow.KeepFor(_m.Changes));
            if (_m.Kind == ReportKind.Summary) Mark("Changes since the last scan");
            _f.Table(_m.Changes, [CellKind.Muted, CellKind.Text]);
        }
    }

    /// <summary>Severity distribution (stacked bar) and, with history, the score trend — side by side.</summary>
    private void DrawChartsRow()
    {
        var trend = _m.ScoreTrend ?? [];
        const double h = 86, gap = 14;
        _f.Ensure(h + 10);
        double top = _f.Y;
        double leftW = trend.Count >= 2 ? (_f.Width - gap) * 0.55 : _f.Width;
        var border = new XPen(UI.ColBorder, 0.8);

        // severity distribution
        G.DrawRectangle(border, M, top, leftW, h);
        T("SEVERITY DISTRIBUTION — OPEN FINDINGS", UI.F7B, Muted, M + 12, top + 18);
        int total = _m.SeverityCounts.Sum(s => s.Count);
        double barX = M + 12, barW = leftW - 24, barY = top + 30, barH = 14;
        if (total == 0) G.DrawRectangle(new XSolidBrush(UI.ColSurface2), barX, barY, barW, barH);
        else
        {
            double x = barX;
            var parts = _m.SeverityCounts.Where(s => s.Count > 0).ToList();
            for (int i = 0; i < parts.Count; i++)
            {
                double w = barW * parts[i].Count / total;
                G.DrawRectangle(new XSolidBrush(_f.Ui.SeverityStyle(UI.ParseSeverity(parts[i].Severity)).border), x, barY, w, barH);
                if (i < parts.Count - 1) G.DrawLine(new XPen(XColors.White, 1.2), x + w, barY, x + w, barY + barH);
                x += w;
            }
        }
        double lx = barX;
        foreach (var (sev, count) in _m.SeverityCounts)
        {
            var label = $"{sev} {count}";
            G.DrawRectangle(new XSolidBrush(_f.Ui.SeverityStyle(UI.ParseSeverity(sev)).border), lx, top + 58, 7, 7);
            T(label, UI.F8, Ink2, lx + 11, top + 65);
            lx += W(label, UI.F8) + 24;
        }

        // score trend
        if (trend.Count >= 2)
        {
            double x0 = M + leftW + gap, w = _f.Width - leftW - gap;
            G.DrawRectangle(border, x0, top, w, h);
            T("SCORE TREND", UI.F7B, Muted, x0 + 12, top + 18);
            var last = trend[^1].Score.ToString(CultureInfo.InvariantCulture);
            T(last, UI.F12B, Ink, x0 + w - 12 - W(last, UI.F12B), top + 20);
            double cx = x0 + 14, cw = w - 28, cy = top + 30, ch = 30;
            int min = Math.Min(trend.Min(p => p.Score), 100), max = Math.Max(trend.Max(p => p.Score), 0);
            if (max - min < 10) { int mid = (max + min) / 2; min = Math.Max(0, mid - 5); max = Math.Min(100, min + 10); }
            var pts = trend.Select((p, i) => new XPoint(cx + cw * i / (trend.Count - 1),
                                                        cy + ch - ch * (p.Score - min) / Math.Max(1.0, max - min))).ToArray();
            G.DrawLine(new XPen(UI.ColBorder, 0.6), cx, cy + ch, cx + cw, cy + ch);
            G.DrawLines(new XPen(UI.ColAccent, 1.6) { LineJoin = XLineJoin.Round, LineCap = XLineCap.Round }, pts);
            G.DrawEllipse(new XSolidBrush(UI.ColAccent), pts[^1].X - 2.5, pts[^1].Y - 2.5, 5, 5);
            T(trend[0].Date, UI.F7, Muted, cx, top + h - 10);
            T(trend[^1].Date, UI.F7, Muted, cx + cw - W(trend[^1].Date, UI.F7), top + h - 10);
        }
        _f.Y = top + h + 14;
    }

    /// <summary>"Fix first" as a ranked list: number, finding, severity, points if fixed.</summary>
    private void DrawFixFirst()
    {
        var t = _m.FixFirst;
        _f.Heading("Fix first — largest score gain", 60);
        Mark("Fix first");
        if (t.Rows.Count == 0) { _f.Paragraph("Nothing to fix first — no rule lowers the score.", UI.F9, Muted); _f.Y += 6; return; }
        const double lineH = 12.5, sevW = 70, ptsW = 58;
        double textX = M + 28, textW = _f.Width - 28 - sevW - ptsW - 8;
        foreach (var row in t.Rows)
        {
            string no = row[0], title = row.Count > 1 ? row[1] : "", pts = row.Count > 2 ? row[2] : "", sev = row.Count > 3 ? row[3] : "";
            var lines = _f.Wrap(title, UI.F9, textW);
            double h = Math.Max(1, lines.Count) * lineH + 12;
            _f.Ensure(h);
            double top = _f.Y;
            G.DrawEllipse(new XSolidBrush(UI.ColNavy), M, top + 5, 17, 17);
            T(no, UI.F7B, White, M + 8.5 - W(no, UI.F7B) / 2, top + 16.5);
            for (int k = 0; k < lines.Count; k++) T(lines[k], UI.F9, Ink, textX, top + 16 + k * lineH);
            if (sev.Length > 0) _f.Ui.SeverityPill(G, UI.ParseSeverity(sev), M + _f.Width - ptsW - sevW + 6, top + 16, _f.SinkAction);
            T(pts, UI.F12B, PdfFlow.Accent, M + _f.Width - W(pts, UI.F12B), top + 17);
            _f.Y += h;
            G.DrawLine(new XPen(UI.ColBorder, 0.6), M, _f.Y, M + _f.Width, _f.Y);
        }
        _f.Y += 8;
    }

    /// <summary>Risk matrix (S21-1): areas × severities, occupied cells tinted in the severity colour.</summary>
    private void DrawRiskMatrix()
    {
        var mx = _m.RiskMatrix;
        const double rowH = 20;
        _f.Heading("Risk matrix — open findings by area and severity", 30 + rowH * mx.Rows.Count);
        Mark("Risk matrix");
        double labelW = _f.Width * 0.34;
        double cellW  = (_f.Width - labelW) / (mx.Severities.Count + 1);
        G.DrawRectangle(new XSolidBrush(UI.ColSurface2), M, _f.Y, _f.Width, 19);
        T("AREA", UI.F7B, Muted, M + 6, _f.Y + 13);
        for (int c = 0; c < mx.Severities.Count; c++)
        {
            var s = mx.Severities[c].ToUpperInvariant();
            T(s, UI.F7B, Muted, M + labelW + c * cellW + cellW / 2 - W(s, UI.F7B) / 2, _f.Y + 13);
        }
        const string totalH = "TOTAL";
        T(totalH, UI.F7B, Muted, M + labelW + mx.Severities.Count * cellW + cellW / 2 - W(totalH, UI.F7B) / 2, _f.Y + 13);
        _f.Y += 21;
        foreach (var row in mx.Rows)
        {
            _f.Ensure(rowH);
            T(_f.Fit(row.Label, UI.F8, labelW - 70), UI.F8, Ink, M + 6, _f.Y + 13.5);
            T(row.Area, UI.Mono8, Muted, M + labelW - 52, _f.Y + 13.5);
            for (int c = 0; c < row.Counts.Count && c < mx.Severities.Count; c++)
            {
                double x = M + labelW + c * cellW;
                var n = row.Counts[c].ToString(CultureInfo.InvariantCulture);
                if (row.Counts[c] > 0)
                {
                    var (bg, border, txt) = _f.Ui.SeverityStyle(UI.ParseSeverity(mx.Severities[c]));
                    G.DrawRectangle(new XSolidBrush(bg), x + 2, _f.Y + 1, cellW - 4, rowH - 2);
                    G.DrawRectangle(new XSolidBrush(border), x + 2, _f.Y + 1, 2.5, rowH - 2);
                    T(n, UI.F9B, txt, x + cellW / 2 - W(n, UI.F9B) / 2, _f.Y + 14);
                }
                else T("–", UI.F8, Muted, x + cellW / 2 - W("–", UI.F8) / 2, _f.Y + 13.5);
            }
            var tot = row.Total.ToString(CultureInfo.InvariantCulture);
            T(tot, UI.F9B, row.Total > 0 ? Ink : Muted, M + labelW + mx.Severities.Count * cellW + cellW / 2 - W(tot, UI.F9B) / 2, _f.Y + 14);
            _f.Y += rowH;
            G.DrawLine(new XPen(UI.ColBorder, 0.6), M, _f.Y, M + _f.Width, _f.Y);
        }
        _f.Y += 10;
    }

    // ── findings ──────────────────────────────────────────────────────────────
    private void DrawFinding(ReportFinding f)
    {
        _f.Continued = "";            // a break before the card must not name the previous finding
        var sev = UI.ParseSeverity(f.Severity);
        var (tint, accent, _) = _f.Ui.SeverityStyle(sev);
        var titleLines = _f.Wrap(f.Title, UI.F12B, _f.Width - 30);
        double headH = 14 + 12 + 8 + titleLines.Count * 16 + 6;
        if (!_f.AtTop) _f.Y += 6;
        _f.Ensure(headH + 70);        // keep the card header with its first lines
        double top = _f.Y;
        G.DrawRectangle(new XSolidBrush(tint), M, top, _f.Width, headH);
        G.DrawRectangle(new XSolidBrush(accent), M, top, 4, headH);
        double x = M + 16, baseline = top + 22;
        x += _f.Ui.SeverityPill(G, sev, x, baseline, _f.SinkAction) + 8;
        T(f.RuleId, UI.Mono8, Ink2, x, baseline - 0.5);
        x += W(f.RuleId, UI.Mono8) + 10;
        int objects = f.Objects.Rows.Count + f.Objects.HiddenRows;
        var count = ReportText.Count(objects, "affected object");
        T(count, UI.F8, Ink2, x, baseline - 0.5);
        if (f.PointsIfFixed > 0)
        {
            var pts = $"+{ReportText.Count(f.PointsIfFixed, "point")} if fixed";
            T(pts, UI.F8B, PdfFlow.Accent, M + _f.Width - 14 - W(pts, UI.F8B), baseline - 0.5);
        }
        double ty = top + 14 + 12 + 8 + 12;
        foreach (var line in titleLines) { T(line, UI.F12B, Ink, M + 16, ty); ty += 16; }
        _f.Y = top + headH + 12;
        _f.Continued = $"{f.RuleId} {f.Title}";   // V-13: a page break inside the card names the finding

        if (f.Description.Length > 0 && f.Description != f.What && f.Description != f.Why)
            _f.Definition("OBSERVATION", f.Description, Ink2);
        if (!string.Equals(f.What.Trim(), f.Title.Trim(), StringComparison.OrdinalIgnoreCase))   // no line that repeats the title
            _f.Definition("WHAT", f.What);
        _f.Definition("WHY IT MATTERS", f.Why);
        _f.Definition("HOW TO FIX", f.HowToFix);
        DrawReferences(f.References);

        if (f.Fix.Length > 0)
        {
            bool wrapped = false;
            CodeLabel("FIX SCRIPT", "PowerShell — review, then run yourself");
            wrapped |= _f.Code(f.Fix);
            _f.Definition("OPERATIONAL RISK", f.FixRisk, _f.Ui.SeverityStyle(FindingSeverity.High).text);
            CodeLabel("UNDO", "");
            wrapped |= _f.Code(f.Revert);
            if (wrapped && !_wrapNoteShown)
            {
                _wrapNoteShown = true;
                _f.Paragraph("Long lines are wrapped for print: a trailing ` | or , continues the command (PowerShell syntax), \"# »\" continues " +
                             "a comment, a leading \"»\" is a printed break that does not run. Copy scripts from the app, not from this PDF.",
                             UI.F7, Muted, 0, 10);
                _f.Y += 6;
            }
        }
        _f.Overline($"AFFECTED OBJECTS ({objects.ToString("N0", CultureInfo.InvariantCulture)})", PdfFlow.KeepFor(f.Objects));
        _f.Table(f.Objects, [CellKind.Text, CellKind.Muted, CellKind.Text, CellKind.Muted]);
        _f.Y += 14;
    }

    private void CodeLabel(string label, string hint)
    {
        _f.Ensure(40);
        T(label, UI.F7B, Muted, M, _f.Y + 9);
        if (hint.Length > 0) T(hint, UI.F7, Muted, M + 96, _f.Y + 9);
        _f.Y += 14;
    }

    /// <summary>References of a finding (S21-2): label as a clickable link, the URL printed below it so
    /// a paper copy still names the source. Long URLs are broken by width (they have no spaces).</summary>
    private void DrawReferences(IReadOnlyList<ReportReference> refs)
    {
        if (refs.Count == 0) return;
        const double labelW = 96;
        bool first = true;
        foreach (var r in refs)
        {
            _f.Ensure(26);
            if (first) T("REFERENCES", UI.F7B, Muted, M, PdfFlow.Base(_f.Y, 13, UI.F9));
            first = false;
            double x = M + labelW;
            var label = _f.Fit(r.Label, UI.F9, _f.Width - labelW);
            double baseline = PdfFlow.Base(_f.Y, 13, UI.F9);
            T(label, UI.F9, PdfFlow.Accent, x, baseline);
            double w = W(label, UI.F9);
            G.DrawLine(new XPen(UI.ColAccent, 0.5), x, baseline + 1.5, x + w, baseline + 1.5);
            var rect = G.Transformer.WorldToDefaultPage(new XRect(x, baseline - 9, w, 12));
            _f.Page.AddWebLink(new PdfRectangle(rect), r.Url);
            _f.Y += 13;
            foreach (var part in _f.Wrap(r.Url, UI.Mono8, _f.Width - labelW))
            {
                _f.Ensure(11);
                T(part, UI.Mono8, Muted, x, _f.Y + 8);
                _f.Y += 11;
            }
            _f.Y += 3;
        }
        _f.Y += 4;
    }

    // ── appendix ──────────────────────────────────────────────────────────────
    /// <summary>Appendix: how the score works, exceptions, failed checks, what was checked, then the user lists.</summary>
    private void DrawAppendix()
    {
        var method = _m.Methodology ?? [];
        if (method.Count > 0)
        {
            _f.Heading("How the score works", 120);
            Mark("How the score works");
            foreach (var line in method) { _f.Bullet(line, UI.F9, Ink2); _f.Y += 3; }
        }

        _f.Heading($"Active exceptions ({_m.Exceptions.Count})");
        Mark("Exceptions and failed checks");
        _f.Table(new ReportTable(["Rule", "Object", "Reason", "Expires", "Created by"],
            _m.Exceptions.Select(e => (IReadOnlyList<string>)new[] { e.RuleId, e.Object, e.Reason, e.Expires, e.CreatedBy }).ToList(), 0, "",
            Weights: [0.13, 0.20, 0.37, 0.12, 0.18]), [CellKind.Mono, CellKind.Text, CellKind.Text, CellKind.Muted, CellKind.Muted],
            "No active exceptions.");

        _f.Heading($"Checks that could not run ({_m.FailedChecks.Count})");
        if (_m.FailedChecks.Count == 0) { _f.Paragraph("All checks ran.", UI.F9, Muted); _f.Y += 6; }
        else _f.Table(new ReportTable(["Rule", "Check", "Reason"],
            _m.FailedChecks.Select(c => (IReadOnlyList<string>)new[] { c.RuleId, c.Category, c.Reason }).ToList(), 0, "",
            Weights: [0.14, 0.28, 0.58]), [CellKind.Mono, CellKind.Text, CellKind.Text]);

        DrawCheckedRules();

        foreach (var (title, table) in _m.Appendix)
        {
            _f.Heading(title, PdfFlow.KeepFor(table));
            _f.Table(table);
        }
    }

    /// <summary>"What was checked" appendix (S21-3): every catalog rule with its status.</summary>
    private void DrawCheckedRules()
    {
        if (_m.CheckedRules.Count == 0) return;
        _f.Heading($"What was checked ({_m.CheckedRules.Count} rules)");
        Mark("What was checked");
        _f.Paragraph(string.Join("   ·   ", _m.CheckedRules.GroupBy(c => c.Status).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}: {g.Count()}")), UI.F9, Ink2);
        _f.Y += 6;
        _f.Table(new ReportTable(["Rule", "Check", "Status", "Note"],
            _m.CheckedRules.Select(c => (IReadOnlyList<string>)new[] { c.RuleId, c.Category, c.Status, c.Reason }).ToList(), 0, "",
            Weights: [0.12, 0.30, 0.16, 0.42]), [CellKind.Mono, CellKind.Text, CellKind.CheckStatus, CellKind.Muted]);
    }

    /// <summary>Left footer text on every page (S18-2) — pure, so tests can check it without parsing the PDF.</summary>
    public static string FooterText(ReportModel m) =>
        $"DirHealth {ShortVersion(m.ToolVersion)} · {m.Domain} · created {m.CreatedLocal}" + (m.Redacted ? " · names redacted" : "");

    /// <summary>"2.9.0+2692a5c…" → "2.9.0" for footers; the full build id stays in the .meta.txt next to the file.</summary>
    internal static string ShortVersion(string version) => version.Split('+')[0];

    /// <summary>Right footer text; drawn in the second pass once the total is known.</summary>
    public static string PageLabel(int n, int total) => $"Page {n} of {total}";

    /// <summary>Page count of a rendered report — for tests.</summary>
    public static int PageCount(string filePath) => PdfSharp.Pdf.IO.PdfReader.Open(filePath, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import).PageCount;
}
