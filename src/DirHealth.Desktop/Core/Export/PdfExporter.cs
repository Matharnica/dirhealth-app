using DirHealth.Desktop.Core.AD.Models;
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
            // Each finding takes ≈ 3 lines (title + description + affected)
            if (y > ui.ContentBottom(page) - 60) NewPage();

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

    public void ExportFullReport(FullReportData data, string filePath)
    {
        var doc  = new PdfDocument();
        doc.Info.Title = "DirHealth — Full AD Health Report";
        var ui   = new PdfPageBuilder();
        int pageNum = 1;

        // ── 1. COVER ─────────────────────────────────────────────────────────────
        {
            var p   = doc.AddPage();
            var gfx = XGraphics.FromPdfPage(p);
            ui.DrawHeader(gfx, p, "Active Directory Health Report");
            ui.DrawFooter(gfx, p, data.Domain, pageNum++);

            double w = p.Width.Point;

            // Blue gradient hero
            const double heroY = PdfPageBuilder.HeaderH;
            const double heroH = 170;
            var grad = new XLinearGradientBrush(
                new XRect(0, heroY, w, heroH),
                PdfPageBuilder.ColGradStart, PdfPageBuilder.ColGradEnd,
                XLinearGradientMode.ForwardDiagonal);
            gfx.DrawRectangle(grad, 0, heroY, w, heroH);

            // Report type label
            gfx.DrawString("ACTIVE DIRECTORY HEALTH REPORT", PdfPageBuilder.F9B,
                new XSolidBrush(XColor.FromArgb(148, 163, 184)),
                PdfPageBuilder.Margin, heroY + 22);

            // Domain
            gfx.DrawString(data.Domain, PdfPageBuilder.F20B,
                XBrushes.White, PdfPageBuilder.Margin, heroY + 44);

            // Date + findings count
            gfx.DrawString(
                $"{DateTime.Now:yyyy-MM-dd HH:mm}  ·  {data.Findings.Count} Findings",
                PdfPageBuilder.F9,
                new XSolidBrush(XColor.FromArgb(148, 163, 184)),
                PdfPageBuilder.Margin, heroY + 58);

            // Score — offsets derived from measured width so score=100 never collides
            XColor scoreColor = ui.ScoreColor(data.Score);
            string scoreText  = data.Score.ToString();
            double scoreW     = gfx.MeasureString(scoreText, PdfPageBuilder.F56B).Width;
            gfx.DrawString(scoreText, PdfPageBuilder.F56B,
                new XSolidBrush(scoreColor),
                PdfPageBuilder.Margin, heroY + 140);

            var suffix16 = new XFont("Arial", 16, XFontStyleEx.Regular);
            gfx.DrawString("/100", suffix16,
                new XSolidBrush(XColor.FromArgb(148, 163, 184)),
                PdfPageBuilder.Margin + scoreW + 8, heroY + 135);

            double suffixW = gfx.MeasureString("/100 ", suffix16).Width;
            gfx.DrawString(ui.ScoreLabel(data.Score), PdfPageBuilder.F9B,
                new XSolidBrush(scoreColor),
                PdfPageBuilder.Margin + scoreW + 8 + suffixW, heroY + 135);

            // Severity summary strip
            const double stripY = heroY + heroH;
            const double stripH = 52;
            gfx.DrawRectangle(new XSolidBrush(PdfPageBuilder.ColFooterBg),
                0, stripY, w, stripH);
            gfx.DrawLine(new XPen(PdfPageBuilder.ColBorder, 0.5),
                0, stripY + stripH, w, stripY + stripH);

            int    critical = data.Findings.Count(f => f.Severity == FindingSeverity.Critical);
            int    high     = data.Findings.Count(f => f.Severity == FindingSeverity.High);
            int    medium   = data.Findings.Count(f => f.Severity == FindingSeverity.Medium);
            int    low      = data.Findings.Count(f => f.Severity != FindingSeverity.Critical
                                                      && f.Severity != FindingSeverity.High
                                                      && f.Severity != FindingSeverity.Medium);
            var    counts   = new[] { critical, high, medium, low };
            var    labels   = new[] { "Critical", "High", "Medium", "Low" };
            var    colors   = new[] {
                PdfPageBuilder.ColCritical, PdfPageBuilder.ColHigh,
                PdfPageBuilder.ColMedium,   PdfPageBuilder.ColLow };

            double colW = w / 4;
            for (int i = 0; i < 4; i++)
            {
                double cx    = i * colW;
                string cntTx = counts[i].ToString();
                double cntW  = gfx.MeasureString(cntTx, new XFont("Arial", 22, XFontStyleEx.Bold)).Width;
                gfx.DrawString(cntTx, new XFont("Arial", 22, XFontStyleEx.Bold),
                    new XSolidBrush(colors[i]),
                    cx + (colW - cntW) / 2, stripY + 30);

                double lblW = gfx.MeasureString(labels[i], PdfPageBuilder.F9B).Width;
                gfx.DrawString(labels[i], PdfPageBuilder.F9B,
                    new XSolidBrush(colors[i]),
                    cx + (colW - lblW) / 2, stripY + 44);

                if (i < 3)
                    gfx.DrawLine(new XPen(PdfPageBuilder.ColBorder, 0.5),
                        cx + colW, stripY + 8, cx + colW, stripY + stripH - 8);
            }
        }

        // ── 2. FINDINGS ──────────────────────────────────────────────────────────
        {
            var page = doc.AddPage();
            var gfx  = XGraphics.FromPdfPage(page);
            ui.DrawHeader(gfx, page, "Findings", data.Domain);
            ui.DrawFooter(gfx, page, data.Domain, pageNum++);
            double y = ui.ContentTop + 10;

            double[] colX   = { PdfPageBuilder.Margin, PdfPageBuilder.Margin + 210,
                                 PdfPageBuilder.Margin + 360, PdfPageBuilder.Margin + 440 };
            string[] colHdr = { "TITLE", "CATEGORY", "SEVERITY", "COUNT" };

            void FindingsNewPage()
            {
                page = doc.AddPage();
                gfx  = XGraphics.FromPdfPage(page);
                ui.DrawHeader(gfx, page, "Findings", data.Domain);
                ui.DrawFooter(gfx, page, data.Domain, pageNum++);
                y = ui.ContentTop + 10;
                y = ui.DrawColHeaders(gfx, page, y, colX, colHdr);
            }

            y = ui.DrawColHeaders(gfx, page, y, colX, colHdr);

            foreach (var f in data.Findings)
            {
                if (y > ui.ContentBottom(page) - 30) FindingsNewPage();
                var (bg, border, txt) = ui.SeverityStyle(f.Severity);
                y = ui.DrawRow(gfx, PdfPageBuilder.Margin, y,
                    ui.ContentWidth(page), border, bg,
                    (g, baseline) =>
                    {
                        g.DrawString(f.Title,              PdfPageBuilder.F10B, txt,                            colX[0] + 10, baseline);
                        g.DrawString(f.Category,           PdfPageBuilder.F9,   new XSolidBrush(PdfPageBuilder.ColBodyText), colX[1],      baseline);
                        g.DrawString(f.Severity.ToString(),PdfPageBuilder.F9B,  txt,                            colX[2],      baseline);
                        g.DrawString(f.Count.ToString(),   PdfPageBuilder.F9,   new XSolidBrush(PdfPageBuilder.ColBodyText), colX[3],      baseline);
                    });
            }
            if (data.Findings.Count == 0)
                gfx.DrawString("No findings — AD environment looks healthy!",
                    PdfPageBuilder.F10, new XSolidBrush(PdfPageBuilder.ColLow),
                    PdfPageBuilder.Margin, y + 16);
        }

        // ── 3. INACTIVE USERS ────────────────────────────────────────────────────
        {
            var page = doc.AddPage();
            var gfx  = XGraphics.FromPdfPage(page);
            ui.DrawHeader(gfx, page, "Inactive Users", data.Domain);
            ui.DrawFooter(gfx, page, data.Domain, pageNum++);
            double y = ui.ContentTop + 10;

            double[] colX   = { PdfPageBuilder.Margin, PdfPageBuilder.Margin + 180,
                                 PdfPageBuilder.Margin + 310 };
            string[] colHdr = { "NAME", "LAST LOGON", "OU" };

            void InactiveUsersNewPage()
            {
                page = doc.AddPage();
                gfx  = XGraphics.FromPdfPage(page);
                ui.DrawHeader(gfx, page, "Inactive Users", data.Domain);
                ui.DrawFooter(gfx, page, data.Domain, pageNum++);
                y = ui.ContentTop + 10;
                y = ui.DrawColHeaders(gfx, page, y, colX, colHdr);
            }

            y = ui.DrawColHeaders(gfx, page, y, colX, colHdr);
            var bodyBrush = new XSolidBrush(PdfPageBuilder.ColBodyText);

            foreach (var u in data.InactiveUsers)
            {
                if (y > ui.ContentBottom(page) - 30) InactiveUsersNewPage();
                y = ui.DrawRow(gfx, PdfPageBuilder.Margin, y,
                    ui.ContentWidth(page), PdfPageBuilder.ColBorder, PdfPageBuilder.ColRowLow,
                    (g, baseline) =>
                    {
                        g.DrawString(u.DisplayName,                                   PdfPageBuilder.F10,  bodyBrush, colX[0] + 10, baseline);
                        g.DrawString(u.LastLogon?.ToString("yyyy-MM-dd") ?? "Never",  PdfPageBuilder.Mono9, bodyBrush, colX[1],      baseline);
                        g.DrawString(DnHelper.OuFromDn(u.DistinguishedName),          PdfPageBuilder.F9,   bodyBrush, colX[2],      baseline);
                    });
            }
            if (data.InactiveUsers.Count == 0)
                gfx.DrawString("No inactive users.", PdfPageBuilder.F10,
                    new XSolidBrush(PdfPageBuilder.ColLow), PdfPageBuilder.Margin, y + 16);
        }

        // ── 4. EXPIRING PASSWORDS ────────────────────────────────────────────────
        {
            var page = doc.AddPage();
            var gfx  = XGraphics.FromPdfPage(page);
            ui.DrawHeader(gfx, page, "Expiring Passwords", data.Domain);
            ui.DrawFooter(gfx, page, data.Domain, pageNum++);
            double y = ui.ContentTop + 10;

            double[] colX   = { PdfPageBuilder.Margin, PdfPageBuilder.Margin + 180,
                                 PdfPageBuilder.Margin + 360, PdfPageBuilder.Margin + 460 };
            string[] colHdr = { "NAME", "EMAIL", "EXPIRES", "DAYS" };

            void PasswordsNewPage()
            {
                page = doc.AddPage();
                gfx  = XGraphics.FromPdfPage(page);
                ui.DrawHeader(gfx, page, "Expiring Passwords", data.Domain);
                ui.DrawFooter(gfx, page, data.Domain, pageNum++);
                y = ui.ContentTop + 10;
                y = ui.DrawColHeaders(gfx, page, y, colX, colHdr);
            }

            y = ui.DrawColHeaders(gfx, page, y, colX, colHdr);

            foreach (var u in data.ExpiringPasswords)
            {
                if (y > ui.ContentBottom(page) - 30) PasswordsNewPage();
                bool expired = u.DaysUntilPasswordExpiry.HasValue && u.DaysUntilPasswordExpiry.Value <= 0;
                bool warning = u.DaysUntilPasswordExpiry.HasValue && u.DaysUntilPasswordExpiry.Value <= 14 && !expired;
                var (bg, border) = expired ? (PdfPageBuilder.ColRowCrit, PdfPageBuilder.ColCritical)
                                 : warning ? (PdfPageBuilder.ColRowHigh, PdfPageBuilder.ColHigh)
                                 :           (PdfPageBuilder.ColRowLow,  PdfPageBuilder.ColBorder);
                var txt = new XSolidBrush(expired ? PdfPageBuilder.ColCritical
                                        : warning ? PdfPageBuilder.ColHigh
                                        :           PdfPageBuilder.ColBodyText);
                y = ui.DrawRow(gfx, PdfPageBuilder.Margin, y,
                    ui.ContentWidth(page), border, bg,
                    (g, baseline) =>
                    {
                        g.DrawString(u.DisplayName,                                      PdfPageBuilder.F10,   txt, colX[0] + 10, baseline);
                        g.DrawString(u.Email ?? "",                                      PdfPageBuilder.F9,    txt, colX[1],      baseline);
                        g.DrawString(u.PasswordExpires?.ToString("yyyy-MM-dd") ?? "",    PdfPageBuilder.Mono9, txt, colX[2],      baseline);
                        g.DrawString(u.DaysUntilPasswordExpiry?.ToString() ?? "N/A",     PdfPageBuilder.F9,    txt, colX[3],      baseline);
                    });
            }
            if (data.ExpiringPasswords.Count == 0)
                gfx.DrawString("No expiring passwords.", PdfPageBuilder.F10,
                    new XSolidBrush(PdfPageBuilder.ColLow), PdfPageBuilder.Margin, y + 16);
        }

        // ── 5. DOMAIN ADMINS ─────────────────────────────────────────────────────
        {
            var page = doc.AddPage();
            var gfx  = XGraphics.FromPdfPage(page);
            ui.DrawHeader(gfx, page, "Domain Admins", data.Domain);
            ui.DrawFooter(gfx, page, data.Domain, pageNum++);
            double y = ui.ContentTop + 20;

            if (data.DomainAdmins.Count > 5)
            {
                string warning = $"⚠ {data.DomainAdmins.Count} Domain Admin accounts detected — review regularly";
                gfx.DrawRectangle(new XSolidBrush(PdfPageBuilder.ColRowCrit),
                    PdfPageBuilder.Margin, y - 14, ui.ContentWidth(page), 20);
                gfx.DrawRectangle(new XSolidBrush(PdfPageBuilder.ColCritical),
                    PdfPageBuilder.Margin, y - 14, 3, 20);
                gfx.DrawString(warning, PdfPageBuilder.F9,
                    new XSolidBrush(PdfPageBuilder.ColCritical),
                    PdfPageBuilder.Margin + 10, y);
                y += 26;
            }

            var bodyBrush = new XSolidBrush(PdfPageBuilder.ColBodyText);
            foreach (var admin in data.DomainAdmins)
            {
                if (y > ui.ContentBottom(page) - 30)
                {
                    page = doc.AddPage();
                    gfx  = XGraphics.FromPdfPage(page);
                    ui.DrawHeader(gfx, page, "Domain Admins", data.Domain);
                    ui.DrawFooter(gfx, page, data.Domain, pageNum++);
                    y = ui.ContentTop + 20;
                }
                gfx.DrawString($"● {admin}", PdfPageBuilder.F10, bodyBrush,
                    PdfPageBuilder.Margin + 8, y);
                y += 18;
            }
            if (data.DomainAdmins.Count == 0)
                gfx.DrawString("No domain admins found.", PdfPageBuilder.F10,
                    new XSolidBrush(PdfPageBuilder.ColLow), PdfPageBuilder.Margin, y);
        }

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

        double y        = ui.ContentTop + 40 + 16;
        double contentW = ui.ContentWidth(page);

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
            y = ui.DrawRow(gfx, PdfPageBuilder.Margin, y, contentW, border, bg,
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

    public void ExportUserDetail(AdUser user, IEnumerable<string> groups, string filePath)
    {
        var doc   = new PdfDocument();
        doc.Info.Title = $"DirHealth — User: {user.DisplayName}";

        var page  = doc.AddPage();
        var gfx   = XGraphics.FromPdfPage(page);
        var fontH = new XFont("Arial", 18, XFontStyleEx.Bold);
        var fontB = new XFont("Arial", 11, XFontStyleEx.Bold);
        var fontR = new XFont("Arial", 10, XFontStyleEx.Regular);
        var fontS = new XFont("Arial",  9, XFontStyleEx.Regular);

        double y      = 40;
        double margin = 40;
        double pw     = page.Width.Point - margin * 2;

        gfx.DrawString("DirHealth — User Profile", fontH, XBrushes.Black, margin, y);
        y += 24;
        gfx.DrawString($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", fontR, XBrushes.Gray, margin, y);
        y += 8;
        gfx.DrawLine(XPens.LightGray, margin, y, margin + pw, y);
        y += 20;

        void Row(string label, string value)
        {
            gfx.DrawString(label, fontB, XBrushes.DarkGray, margin, y);
            gfx.DrawString(value, fontR, XBrushes.Black, margin + 150, y);
            y += 18;
        }

        Row("Display Name:",           user.DisplayName);
        Row("SAM Account:",            user.SamAccountName);
        Row("Email:",                  user.Email);
        Row("OU:",                     DnHelper.OuFromDn(user.DistinguishedName));
        Row("Status:",                 user.IsEnabled ? "Enabled" : "Disabled");
        Row("Password Never Expires:", user.PasswordNeverExpires ? "Yes" : "No");
        Row("Password Last Set:",      user.PasswordLastSet?.ToString("yyyy-MM-dd") ?? "N/A");
        Row("Password Expires:",       user.PasswordExpires?.ToString("yyyy-MM-dd") ?? "Never");

        y += 10;
        gfx.DrawLine(XPens.LightGray, margin, y, margin + pw, y);
        y += 16;
        gfx.DrawString("Group Memberships", fontB, XBrushes.Black, margin, y);
        y += 18;

        var groupList = groups.ToList();
        foreach (var g in groupList)
        {
            if (y > page.Height.Point - 60)
            {
                page = doc.AddPage();
                gfx  = XGraphics.FromPdfPage(page);
                y    = 40;
            }
            gfx.DrawString($"• {g}", fontS, XBrushes.DarkGray, margin + 8, y);
            y += 15;
        }
        if (groupList.Count == 0)
            gfx.DrawString("No group memberships found.", fontS, XBrushes.DarkGray, margin + 8, y);

        doc.Save(filePath);
    }

}

file sealed class PdfPageBuilder
{
    // ── Colours ──────────────────────────────────────────────────────────────
    internal static readonly XColor ColHeaderBg    = XColor.FromArgb( 15,  23,  42); // #0f172a
    internal static readonly XColor ColGradStart   = XColor.FromArgb( 30,  58, 138); // #1e3a8a
    internal static readonly XColor ColGradEnd     = XColor.FromArgb( 59, 130, 246); // #3B82F6
    internal static readonly XColor ColScoreGood   = XColor.FromArgb(125, 255, 179); // #7DFFB3
    internal static readonly XColor ColScoreWarn   = XColor.FromArgb(252, 211,  77); // #FCD34D
    internal static readonly XColor ColScoreCrit   = XColor.FromArgb(252, 165, 165); // #FCA5A5
    internal static readonly XColor ColCritical    = XColor.FromArgb(220,  38,  38); // #dc2626
    internal static readonly XColor ColHigh        = XColor.FromArgb(217, 119,   6); // #d97706
    internal static readonly XColor ColMedium      = XColor.FromArgb(202, 138,   4); // #ca8a04
    internal static readonly XColor ColLow         = XColor.FromArgb(107, 114, 128); // #6b7280
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

    private readonly XImage _logo;

    internal PdfPageBuilder()
    {
        var sri = Application.GetResourceStream(
            new Uri("pack://application:,,,/Resources/icon_128.png"))
            ?? throw new InvalidOperationException(
                "DirHealth logo resource not found: Resources/icon_128.png");
        using var stream = sri.Stream;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        _logo = XImage.FromStream(() => new MemoryStream(bytes));
    }

    internal double ContentTop                     => HeaderH;
    internal double ContentBottom(PdfPage p)       => p.Height.Point - FooterH;
    internal double ContentWidth(PdfPage p)        => p.Width.Point - Margin * 2;

    internal void DrawHeader(XGraphics gfx, PdfPage page, string pageTitle, string rightText = "")
    {
        double w = page.Width.Point;
        gfx.DrawRectangle(new XSolidBrush(ColHeaderBg), 0, 0, w, HeaderH);
        gfx.DrawImage(_logo, 10, 5, 18, 18);

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

    internal bool IsPrivilegedGroup(string name)
    {
        var lower = name.ToLowerInvariant();
        return lower is "domain admins" or "schema admins" or "enterprise admins"
            or "administrators" or "backup operators" or "account operators"
            or "server operators" or "group policy creator owners";
    }

    internal void DrawGroupBadge(XGraphics gfx, ref double x, double y, string groupName)
    {
        bool   priv   = IsPrivilegedGroup(groupName);
        XColor bgCol  = priv ? XColor.FromArgb(254, 242, 242) : XColor.FromArgb(239, 246, 255);
        XColor txtCol = priv ? XColor.FromArgb(220,  38,  38) : XColor.FromArgb( 29,  78, 216);
        XColor bdCol  = priv ? XColor.FromArgb(254, 202, 202) : XColor.FromArgb(191, 219, 254);

        double tw     = gfx.MeasureString(groupName, F9).Width;
        double bw     = tw + 12;
        const double bh = 14;

        gfx.DrawRectangle(new XSolidBrush(bgCol), x, y - 11, bw, bh);
        gfx.DrawRectangle(new XPen(bdCol, 0.5),   x, y - 11, bw, bh);
        gfx.DrawString(groupName, F9, new XSolidBrush(txtCol), x + 6, y);
        x += bw + 5;
    }
}
