using DirHealth.Desktop.Core;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Services;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using System.IO;
using System.Windows;

namespace DirHealth.Desktop.Core.Export;

public class PdfExporter
{
    public void ExportReport(List<AdFinding> findings, int score, string filePath)
    {
        var doc = new PdfDocument();
        doc.Info.Title = "DirHealth AD Report";
        var ui      = new PdfPageBuilder();
        int pageNum = 1;

        var page = doc.AddPage();
        var gfx  = XGraphics.FromPdfPage(page);
        ui.DrawHeader(gfx, page, "AD Health Report");
        ui.DrawFooter(gfx, page, "—", pageNum++);

        // Title strip (page 1 only)
        ui.DrawTitleStrip(gfx, page,
            "AD Health Report",
            $"Score: {score}/100  ·  {ui.ScoreLabel(score)}",
            $"{DateTime.Now:yyyy-MM-dd HH:mm}  ·  {findings.Count} findings");

        double y        = ui.ContentTop + 40 + 16; // below title strip
        double contentW = ui.ContentWidth(page);

        void NewPage()
        {
            page = doc.AddPage();
            gfx  = XGraphics.FromPdfPage(page);
            ui.DrawHeader(gfx, page, "AD Health Report");
            ui.DrawFooter(gfx, page, "—", pageNum++);
            y = ui.ContentTop + 16;
        }

        foreach (var finding in findings)
        {
            // Each finding takes ≈ title + description + wrapped remediation + affected
            if (y > ui.ContentBottom(page) - 90) NewPage();

            var (bg, border, txt) = ui.SeverityStyle(finding.Severity);

            // Draw the background + left border
            gfx.DrawRectangle(new XSolidBrush(bg),
                PdfPageBuilder.Margin, y, contentW, PdfPageBuilder.RowH);
            gfx.DrawRectangle(new XSolidBrush(border),
                PdfPageBuilder.Margin, y, 3, PdfPageBuilder.RowH);

            gfx.DrawString(
                $"[{finding.Severity}]  {finding.Title}",
                PdfPageBuilder.F10B, txt,
                PdfPageBuilder.Margin + 10, y + PdfPageBuilder.RowH - 6);
            y += PdfPageBuilder.RowH;

            // Description
            gfx.DrawString(finding.Description, PdfPageBuilder.F9,
                new XSolidBrush(PdfPageBuilder.ColLabelGray),
                PdfPageBuilder.Margin + 14, y + 12);
            y += 16;

            // Remediation (wrapped) — the concrete fix, printed under the description
            y = ui.DrawRemediation(gfx, page, finding, y);

            // Affected objects
            if (finding.AffectedObjects.Count > 0)
            {
                var preview = string.Join(", ", finding.AffectedObjects.Take(5));
                if (finding.AffectedObjects.Count > 5)
                    preview += $" (+{finding.AffectedObjects.Count - 5} more)";
                gfx.DrawString(preview, PdfPageBuilder.F9,
                    new XSolidBrush(PdfPageBuilder.ColLow),
                    PdfPageBuilder.Margin + 14, y + 12);
                y += 16;
            }
            y += PdfPageBuilder.RowGap + 4;
        }

        if (findings.Count == 0)
            gfx.DrawString(
                "No findings — AD environment looks healthy!",
                PdfPageBuilder.F10, new XSolidBrush(PdfPageBuilder.ColLow),
                PdfPageBuilder.Margin, y + 16);

        doc.Save(filePath);
    }

    public void ExportPasswordReport(IEnumerable<AdUser> users, string filePath)
    {
        var list    = users.ToList();
        var doc     = new PdfDocument();
        doc.Info.Title = "DirHealth — Password Expiry Report";
        var ui      = new PdfPageBuilder();
        int pageNum = 1;
        int expired = list.Count(u => u.DaysUntilPasswordExpiry.HasValue && u.DaysUntilPasswordExpiry.Value <= 0);

        var page = doc.AddPage();
        var gfx  = XGraphics.FromPdfPage(page);
        ui.DrawHeader(gfx, page, "Password Expiry Report");
        ui.DrawFooter(gfx, page, "—", pageNum++);

        ui.DrawTitleStrip(gfx, page,
            "Password Expiry Report",
            $"{list.Count} user(s)",
            $"{expired} expired  ·  {DateTime.Now:yyyy-MM-dd}");

        double y = ui.ContentTop + 40 + 16;

        double[] colX   = { PdfPageBuilder.Margin, PdfPageBuilder.Margin + 180,
                             PdfPageBuilder.Margin + 370, PdfPageBuilder.Margin + 460 };
        string[] colHdr = { "NAME", "EMAIL", "EXPIRES", "DAYS LEFT" };

        void Headers()
        {
            page = doc.AddPage();
            gfx  = XGraphics.FromPdfPage(page);
            ui.DrawHeader(gfx, page, "Password Expiry Report");
            ui.DrawFooter(gfx, page, "—", pageNum++);
            y = ui.ContentTop + 16;
            y = ui.DrawColHeaders(gfx, page, y, colX, colHdr);
        }

        y = ui.DrawColHeaders(gfx, page, y, colX, colHdr);

        foreach (var u in list)
        {
            if (y > ui.ContentBottom(page) - 30) Headers();

            bool isExpired = u.DaysUntilPasswordExpiry.HasValue && u.DaysUntilPasswordExpiry.Value <= 0;
            bool isWarning = u.DaysUntilPasswordExpiry.HasValue && u.DaysUntilPasswordExpiry.Value <= 14 && !isExpired;

            var (bg, border) = isExpired ? (PdfPageBuilder.ColRowCrit, PdfPageBuilder.ColCritical)
                             : isWarning ? (PdfPageBuilder.ColRowHigh, PdfPageBuilder.ColHigh)
                             :             (PdfPageBuilder.ColRowLow,  PdfPageBuilder.ColBorder);
            var txt = new XSolidBrush(isExpired ? PdfPageBuilder.ColCritical
                                    : isWarning ? PdfPageBuilder.ColHigh
                                    :             PdfPageBuilder.ColBodyText);
            y = ui.DrawRow(gfx, PdfPageBuilder.Margin, y, ui.ContentWidth(page), border, bg,
                (g, baseline) =>
                {
                    g.DrawString(u.DisplayName,                                    PdfPageBuilder.F10,   txt, colX[0] + 10, baseline);
                    g.DrawString(u.Email ?? "",                                    PdfPageBuilder.F9,    txt, colX[1],      baseline);
                    g.DrawString(u.PasswordExpires?.ToString("yyyy-MM-dd") ?? "",  PdfPageBuilder.Mono9, txt, colX[2],      baseline);
                    g.DrawString(u.DaysUntilPasswordExpiry?.ToString() ?? "N/A",   PdfPageBuilder.F9,    txt, colX[3],      baseline);
                });
        }

        if (list.Count == 0)
            gfx.DrawString("No users with expiring passwords.", PdfPageBuilder.F10,
                new XSolidBrush(PdfPageBuilder.ColLow), PdfPageBuilder.Margin, y + 16);

        doc.Save(filePath);
    }

    /// <summary>
    /// Single-user security dossier (S15-5): attributes, Tier 0 status, every group with its
    /// membership path, affected findings with remediation, and audit metadata. Page breaks
    /// re-draw header/footer and the section title ("continued").
    /// </summary>
    public void ExportUserDossier(UserDossier d, string filePath) => ExportUserDossier(d, filePath, null);

    /// <param name="textSink">Receives every body text line exactly as drawn — lets tests check the
    /// PDF's content without parsing PDF streams.</param>
    internal void ExportUserDossier(UserDossier d, string filePath, Action<string>? textSink)
    {
        var doc = new PdfDocument();
        doc.Info.Title = $"DirHealth — User dossier: {d.User.SamAccountName}";
        var ui      = new PdfPageBuilder();
        int pageNum = 1;
        PdfPage   page = null!;
        XGraphics gfx  = null!;
        double    y    = 0;
        var label = new XSolidBrush(PdfPageBuilder.ColLabelGray);
        var body  = new XSolidBrush(PdfPageBuilder.ColBodyText);
        var warn  = new XSolidBrush(PdfPageBuilder.ColCritical);
        double x  = PdfPageBuilder.Margin;
        string section = "";

        void NewPage()
        {
            page = doc.AddPage();
            gfx  = XGraphics.FromPdfPage(page);
            ui.DrawHeader(gfx, page, "User Dossier", d.Context.Domain);
            ui.DrawFooter(gfx, page, d.Context.Domain, pageNum++);
            y = ui.ContentTop + 24;
            if (section.Length > 0)
            {
                Draw(section + " (continued)", PdfPageBuilder.F9B, label, x, y);
                y += 16;
            }
        }
        void Draw(string text, XFont font, XBrush brush, double px, double py)
        {
            textSink?.Invoke(text);
            gfx.DrawString(text, font, brush, px, py);
        }
        void Ensure(double h) { if (y + h > ui.ContentBottom(page) - 10) NewPage(); }
        void Section(string title)
        {
            section = "";
            Ensure(40);
            y += 8;
            Draw(title, PdfPageBuilder.F11B, body, x, y);
            y += 6;
            gfx.DrawLine(new XPen(PdfPageBuilder.ColBorder, 1), x, y, x + ui.ContentWidth(page), y);
            y += 14;
            section = title;
        }
        void Wrapped(string text, XFont font, XBrush brush, double indent, double lineH)
        {
            foreach (var line in PdfPageBuilder.WrapText(gfx, text, font, ui.ContentWidth(page) - indent))
            {
                Ensure(lineH);
                Draw(line, font, brush, x + indent, y);
                y += lineH;
            }
        }

        NewPage();
        ui.DrawTitleStrip(gfx, page, "User Dossier", d.User.DisplayName.Length > 0 ? d.User.DisplayName : d.User.SamAccountName,
            d.IsTier0 ? $"{d.User.SamAccountName} · TIER 0 MEMBER" : d.User.SamAccountName);
        y = ui.ContentTop + 40 + 24;

        Section("ATTRIBUTES");
        const double valueX = 190;
        foreach (var row in d.Attributes)
        {
            var lines = PdfPageBuilder.WrapText(gfx, row.Value, PdfPageBuilder.F9, ui.ContentWidth(page) - valueX);
            Ensure(13 * Math.Max(1, lines.Count) + 2);
            Draw(row.Label, PdfPageBuilder.F9B, label, x, y);
            foreach (var line in lines.DefaultIfEmpty(""))
            {
                Draw(line, PdfPageBuilder.F9, row.Warn ? warn : body, x + valueX, y);
                y += 13;
            }
            y += 2;
        }

        Section($"GROUP MEMBERSHIPS ({d.Groups.Count})");
        if (d.Groups.Count == 0) Wrapped("No group memberships.", PdfPageBuilder.F9, label, 0, 13);
        foreach (var g in d.Groups)
        {
            Ensure(28);
            var tag = g.KindLabel + (g.IsTier0 ? " · TIER 0" : "");
            Draw(g.Name, PdfPageBuilder.F10B, g.IsTier0 ? warn : body, x, y);
            Draw(tag, PdfPageBuilder.F9, label, x + ui.ContentWidth(page) - gfx.MeasureString(tag, PdfPageBuilder.F9).Width, y);
            y += 13;
            Wrapped(g.Path, PdfPageBuilder.Mono9, label, 10, 12);
            y += 4;
        }

        Section($"AFFECTED BY FINDINGS ({d.Findings.Count})");
        if (d.Findings.Count == 0) Wrapped("This user appears in no finding of the last scan.", PdfPageBuilder.F9, label, 0, 13);
        foreach (var f in d.Findings)
        {
            Ensure(30);
            var (_, border, text) = ui.SeverityStyle(f.Severity);
            gfx.DrawRectangle(new XSolidBrush(border), x, y - 9, 3, 12);
            Draw($"{f.Severity.ToString().ToUpperInvariant()}  {f.Title}", PdfPageBuilder.F10B, text, x + 8, y);
            y += 14;
            if (!string.IsNullOrEmpty(f.Remediation))
                Wrapped("→ Fix: " + f.Remediation, PdfPageBuilder.F9, new XSolidBrush(PdfPageBuilder.ColGradEnd), 8, 12);
            y += 4;
        }

        Section("REPORT METADATA");
        foreach (var (k, v) in new[]
        {
            ("Domain",            d.Context.Domain),
            ("Domain controller", d.Context.DomainController),
            ("Run by",            d.Context.User),
            ("Last scan",         d.ScanTime),
            ("Created (UTC)",     d.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss'Z'")),
            ("Tool",              $"DirHealth {d.Version}"),
            ("Integrity",         "SHA-256 of this file: see the .meta.txt next to it"),
        })
        {
            Ensure(14);
            Draw(k, PdfPageBuilder.F9B, label, x, y);
            Draw(v, PdfPageBuilder.F9, body, x + valueX, y);
            y += 13;
        }

        doc.Save(filePath);
    }
}

internal sealed class PdfPageBuilder
{
    // ── Colours ──────────────────────────────────────────────────────────────
    internal static readonly XColor ColHeaderBg    = XColor.FromArgb( 15,  23,  42); // #0f172a
    internal static readonly XColor ColGradStart   = XColor.FromArgb( 30,  58, 138); // #1e3a8a
    internal static readonly XColor ColGradEnd     = XColor.FromArgb( 59, 130, 246); // #3B82F6
    internal static readonly XColor ColScoreGood   = XColor.FromArgb(125, 255, 179); // #7DFFB3
    internal static readonly XColor ColScoreWarn   = XColor.FromArgb(252, 211,  77); // #FCD34D
    internal static readonly XColor ColScoreCrit   = XColor.FromArgb(252, 165, 165); // #FCA5A5
    // Severity accents: single source shared with the app (A9, S14-6)
    internal static readonly XColor ColCritical    = FromRgb(SeverityPalette.CriticalRgb);
    internal static readonly XColor ColHigh        = FromRgb(SeverityPalette.HighRgb);
    internal static readonly XColor ColMedium      = FromRgb(SeverityPalette.MediumRgb);
    internal static readonly XColor ColLow         = FromRgb(SeverityPalette.LowRgb);

    private static XColor FromRgb((byte R, byte G, byte B) c) => XColor.FromArgb(c.R, c.G, c.B);
    internal static readonly XColor ColBodyText    = XColor.FromArgb( 17,  24,  39); // #111827
    internal static readonly XColor ColLabelGray   = XColor.FromArgb(107, 114, 128); // #6b7280
    internal static readonly XColor ColFooterBg    = XColor.FromArgb(248, 250, 252); // #f8fafc
    internal static readonly XColor ColFooterText  = XColor.FromArgb(203, 213, 225); // #cbd5e1
    internal static readonly XColor ColBorder      = XColor.FromArgb(226, 232, 240); // #e2e8f0
    internal static readonly XColor ColRowCrit     = XColor.FromArgb(254, 242, 242); // #fef2f2
    internal static readonly XColor ColRowHigh     = XColor.FromArgb(255, 247, 237); // #fff7ed
    internal static readonly XColor ColRowMed      = XColor.FromArgb(254, 252, 232); // #fefce8
    internal static readonly XColor ColRowLow      = XColor.FromArgb(249, 250, 251); // #f9fafb

    // ── Fonts ─────────────────────────────────────────────────────────────────
    internal static readonly XFont F9    = new("Arial",  9, XFontStyleEx.Regular);
    internal static readonly XFont F9B   = new("Arial",  9, XFontStyleEx.Bold);
    internal static readonly XFont F10   = new("Arial", 10, XFontStyleEx.Regular);
    internal static readonly XFont F10B  = new("Arial", 10, XFontStyleEx.Bold);
    internal static readonly XFont F11B  = new("Arial", 11, XFontStyleEx.Bold);
    internal static readonly XFont F14B  = new("Arial", 14, XFontStyleEx.Bold);
    internal static readonly XFont F20B  = new("Arial", 20, XFontStyleEx.Bold);
    internal static readonly XFont F56B  = new("Arial", 56, XFontStyleEx.Bold);
    internal static readonly XFont Mono9 = new("Courier New", 9, XFontStyleEx.Regular);

    // ── Layout ────────────────────────────────────────────────────────────────
    internal const double Margin  = 40;
    internal const double HeaderH = 28;
    internal const double FooterH = 20;
    internal const double RowH    = 20;
    internal const double RowGap  =  3;

    private readonly XImage? _logo;

    internal PdfPageBuilder()
    {
        try
        {
            var sri = Application.GetResourceStream(
                new Uri("pack://application:,,,/Resources/icon_128.png"));
            if (sri != null)
            {
                using var srcStream = sri.Stream;
                using var ms = new MemoryStream();
                srcStream.CopyTo(ms);
                _logo = XImage.FromStream(new MemoryStream(ms.ToArray()));
            }
        }
        catch { /* logo is optional — export continues without it */ }
    }

    internal double ContentTop                     => HeaderH;
    internal double ContentBottom(PdfPage p)       => p.Height.Point - FooterH;
    internal double ContentWidth(PdfPage p)        => p.Width.Point - Margin * 2;

    internal void DrawHeader(XGraphics gfx, PdfPage page, string pageTitle, string rightText = "")
    {
        double w = page.Width.Point;
        gfx.DrawRectangle(new XSolidBrush(ColHeaderBg), 0, 0, w, HeaderH);
        if (_logo != null) gfx.DrawImage(_logo, 10, 5, 18, 18);

        double x = 32;
        gfx.DrawString("DirHealth", F11B,
            new XSolidBrush(XColor.FromArgb(148, 163, 184)), x, 19);
        x += gfx.MeasureString("DirHealth", F11B).Width + 5;

        gfx.DrawString("›", F11B,
            new XSolidBrush(XColor.FromArgb(51, 65, 85)), x, 19);
        x += gfx.MeasureString("› ", F11B).Width;

        gfx.DrawString(pageTitle, F11B, XBrushes.White, x, 19);

        string rt  = string.IsNullOrEmpty(rightText) ? "AD SECURITY SCANNER" : rightText;
        double rtW = gfx.MeasureString(rt, F9).Width;
        gfx.DrawString(rt, F9,
            new XSolidBrush(XColor.FromArgb(71, 85, 105)),
            w - Margin - rtW, 19);
    }

    internal void DrawFooter(XGraphics gfx, PdfPage page, string domain, int pageNumber)
    {
        double w  = page.Width.Point;
        double y0 = page.Height.Point - FooterH;
        gfx.DrawRectangle(new XSolidBrush(ColFooterBg), 0, y0, w, FooterH);
        gfx.DrawLine(new XPen(ColBorder, 0.5), 0, y0, w, y0);

        var brush = new XSolidBrush(ColFooterText);
        gfx.DrawString(
            $"DirHealth · {domain} · {DateTime.Now:yyyy-MM-dd}",
            Mono9, brush, Margin, y0 + 14);

        string pg  = $"Page {pageNumber}";
        double pgW = gfx.MeasureString(pg, Mono9).Width;
        gfx.DrawString(pg, Mono9, brush, w - Margin - pgW, y0 + 14);
    }

    internal void DrawTitleStrip(XGraphics gfx, PdfPage page,
        string label, string title, string meta)
    {
        double w  = page.Width.Point;
        double y0 = HeaderH;
        const double stripH = 40;
        gfx.DrawRectangle(new XSolidBrush(ColGradStart), 0, y0, w, stripH);

        gfx.DrawString(label.ToUpperInvariant(), F9B,
            new XSolidBrush(XColor.FromArgb(148, 163, 184)), Margin, y0 + 13);
        gfx.DrawString(title, F14B, XBrushes.White, Margin, y0 + 30);

        double metaW = gfx.MeasureString(meta, F9).Width;
        gfx.DrawString(meta, F9,
            new XSolidBrush(XColor.FromArgb(148, 163, 184)),
            w - Margin - metaW, y0 + 22);
    }

    internal double DrawRow(XGraphics gfx, double x, double y, double rowW,
        XColor borderColor, XColor bgColor, Action<XGraphics, double> drawCells)
    {
        gfx.DrawRectangle(new XSolidBrush(bgColor),      x,     y, rowW, RowH);
        gfx.DrawRectangle(new XSolidBrush(borderColor),  x,     y, 3,    RowH);
        drawCells(gfx, y + RowH - 6);
        return y + RowH + RowGap;
    }

    internal double DrawColHeaders(XGraphics gfx, PdfPage page,
        double y, double[] colX, string[] labels)
    {
        var labelBrush = new XSolidBrush(ColLabelGray);
        for (int i = 0; i < labels.Length; i++)
            gfx.DrawString(labels[i], F9B, labelBrush, colX[i], y);
        y += 5;
        gfx.DrawLine(new XPen(ColBorder, 2), Margin, y, Margin + ContentWidth(page), y);
        return y + 7;
    }

    // Greedy word-wrap so multi-sentence text (e.g. remediation) fits the content width.
    internal static List<string> WrapText(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) return lines;
        var words = text.Split(' ');
        var current = "";
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (gfx.MeasureString(candidate, font).Width > maxWidth && current.Length > 0)
            {
                lines.Add(current);
                current = word;
            }
            else current = candidate;
        }
        if (current.Length > 0) lines.Add(current);
        return lines;
    }

    // Draws the wrapped remediation guidance under a finding; returns the new y. No-op when empty.
    internal double DrawRemediation(XGraphics gfx, PdfPage page, AdFinding finding, double y)
    {
        if (string.IsNullOrEmpty(finding.Remediation)) return y;
        var fixBrush = new XSolidBrush(ColGradEnd);
        double maxW  = ContentWidth(page) - 24;
        foreach (var line in WrapText(gfx, "→ Fix: " + finding.Remediation, F9, maxW))
        {
            gfx.DrawString(line, F9, fixBrush, Margin + 14, y + 12);
            y += 13;
        }
        return y + 3;
    }

    internal (XColor bg, XColor border, XSolidBrush text) SeverityStyle(FindingSeverity sev) =>
        sev switch
        {
            FindingSeverity.Critical => (ColRowCrit, ColCritical, new XSolidBrush(ColCritical)),
            FindingSeverity.High     => (ColRowHigh, ColHigh,
                                         new XSolidBrush(XColor.FromArgb(146, 64, 14))),
            FindingSeverity.Medium   => (ColRowMed,  ColMedium,
                                         new XSolidBrush(XColor.FromArgb(113, 63, 18))),
            _                        => (ColRowLow,  ColLow, new XSolidBrush(ColBodyText)),
        };

    internal XColor ScoreColor(int score) =>
        score >= 80 ? ColScoreGood :
        score >= 60 ? ColScoreWarn :
                      ColScoreCrit;

    internal string ScoreLabel(int score) =>
        score >= 80 ? "Good" :
        score >= 60 ? "Warning" :
                      "Critical";
}
