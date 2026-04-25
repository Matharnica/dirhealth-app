using DirHealth.Desktop.Core.AD.Models;
using PdfSharp.Pdf;
using PdfSharp.Drawing;

namespace DirHealth.Desktop.Core.Export;

public class PdfExporter
{
    public void ExportReport(List<AdFinding> findings, int score, string filePath)
    {
        var doc  = new PdfDocument();
        doc.Info.Title = "DirHealth AD Report";

        var page      = doc.AddPage();
        var gfx       = XGraphics.FromPdfPage(page);
        var fontH     = new XFont("Arial", 18, XFontStyleEx.Bold);
        var fontB     = new XFont("Arial", 11, XFontStyleEx.Bold);
        var fontR     = new XFont("Arial", 10, XFontStyleEx.Regular);
        var fontS     = new XFont("Arial", 9,  XFontStyleEx.Regular);

        double y         = 40;
        double margin    = 40;
        double pageWidth = page.Width.Point - margin * 2;

        // Header
        gfx.DrawString("DirHealth — AD Health Report", fontH, XBrushes.Black, margin, y);
        y += 30;
        gfx.DrawString($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}", fontR, XBrushes.Gray, margin, y);
        y += 10;
        gfx.DrawLine(XPens.LightGray, margin, y, margin + pageWidth, y);
        y += 20;

        // Score
        gfx.DrawString($"Compliance Score: {score}/100", fontB, XBrushes.Black, margin, y);
        y += 30;

        // Findings
        gfx.DrawString("Findings", fontB, XBrushes.Black, margin, y);
        y += 20;

        foreach (var finding in findings)
        {
            if (y > page.Height.Point - 80)
            {
                page = doc.AddPage();
                gfx  = XGraphics.FromPdfPage(page);
                y    = 40;
            }

            var severityColor = finding.Severity switch
            {
                FindingSeverity.Critical => XBrushes.DarkRed,
                FindingSeverity.High     => XBrushes.Red,
                FindingSeverity.Medium   => XBrushes.DarkOrange,
                _                        => XBrushes.Gray
            };

            gfx.DrawString($"[{finding.Severity}] {finding.Title}", fontB, severityColor, margin, y);
            y += 15;
            gfx.DrawString(finding.Description, fontS, XBrushes.DarkGray, margin + 10, y);
            y += 15;

            if (finding.AffectedObjects.Count > 0)
            {
                var preview = string.Join(", ", finding.AffectedObjects.Take(5));
                if (finding.AffectedObjects.Count > 5) preview += $" (+{finding.AffectedObjects.Count - 5} more)";
                gfx.DrawString(preview, fontS, XBrushes.Gray, margin + 10, y);
                y += 15;
            }
            y += 8;
        }

        if (findings.Count == 0)
            gfx.DrawString("No findings — AD environment looks healthy!", fontR, XBrushes.DarkGreen, margin, y);

        doc.Save(filePath);
    }

    public void ExportFullReport(FullReportData data, string filePath)
    {
        var doc   = new PdfDocument();
        doc.Info.Title = "DirHealth — Full AD Health Report";

        var fontH     = new XFont("Arial", 22, XFontStyleEx.Bold);
        var fontH2    = new XFont("Arial", 14, XFontStyleEx.Bold);
        var fontSub   = new XFont("Arial", 16, XFontStyleEx.Regular);
        var fontScore = new XFont("Arial", 18, XFontStyleEx.Bold);
        var fontB     = new XFont("Arial", 10, XFontStyleEx.Bold);
        var fontR     = new XFont("Arial", 10, XFontStyleEx.Regular);
        var fontS     = new XFont("Arial",  9, XFontStyleEx.Regular);

        const double margin = 40;

        XGraphics NewPage()
        {
            var p = doc.AddPage();
            return XGraphics.FromPdfPage(p);
        }

        double PageWidth(XGraphics g) => g.PageSize.Width - margin * 2;

        // ── 1. COVER ──────────────────────────────────────────────────────────
        var gfx = NewPage();
        double y = 80;

        gfx.DrawString("DirHealth", fontH, XBrushes.Black, margin, y);
        y += 32;
        gfx.DrawString("Active Directory Health Report", fontSub, XBrushes.DarkGray, margin, y);
        y += 48;
        gfx.DrawString($"Domain:    {data.Domain}",              fontR, XBrushes.Black, margin, y); y += 20;
        gfx.DrawString($"Date:      {DateTime.Now:dd.MM.yyyy HH:mm}", fontR, XBrushes.Black, margin, y); y += 20;
        gfx.DrawString($"Findings:  {data.Findings.Count}",      fontR, XBrushes.Black, margin, y); y += 40;

        var scoreBrush = data.Score >= 80 ? XBrushes.DarkGreen
                       : data.Score >= 60 ? XBrushes.DarkOrange
                       : XBrushes.DarkRed;
        gfx.DrawString($"Compliance Score: {data.Score}/100",
            fontScore, scoreBrush, margin, y);

        // ── 2. FINDINGS ───────────────────────────────────────────────────────
        gfx = NewPage();
        y   = 40;
        gfx.DrawString("Findings", fontH2, XBrushes.Black, margin, y); y += 24;
        gfx.DrawLine(XPens.LightGray, margin, y, margin + PageWidth(gfx), y); y += 14;

        double f1 = margin, f2 = margin + 210, f3 = margin + 370, f4 = margin + 450;
        void DrawFindingsHeaders()
        {
            gfx.DrawString("Title",    fontB, XBrushes.Black, f1, y);
            gfx.DrawString("Category", fontB, XBrushes.Black, f2, y);
            gfx.DrawString("Severity", fontB, XBrushes.Black, f3, y);
            gfx.DrawString("Count",    fontB, XBrushes.Black, f4, y);
            y += 16;
        }
        DrawFindingsHeaders();

        foreach (var finding in data.Findings)
        {
            if (y > gfx.PageSize.Height - 60) { gfx = NewPage(); y = 40; DrawFindingsHeaders(); }
            var sb = finding.Severity switch
            {
                FindingSeverity.Critical => XBrushes.DarkRed,
                FindingSeverity.High     => XBrushes.Red,
                FindingSeverity.Medium   => XBrushes.DarkOrange,
                _                        => XBrushes.Gray
            };
            gfx.DrawString(finding.Title,              fontS, sb,            f1, y);
            gfx.DrawString(finding.Category,           fontS, XBrushes.Black, f2, y);
            gfx.DrawString(finding.Severity.ToString(), fontS, sb,            f3, y);
            gfx.DrawString(finding.Count.ToString(),   fontS, XBrushes.Black, f4, y);
            y += 14;
        }
        if (data.Findings.Count == 0)
            gfx.DrawString("No findings.", fontR, XBrushes.DarkGreen, margin, y);

        // ── 3. INACTIVE USERS ─────────────────────────────────────────────────
        gfx = NewPage();
        y   = 40;
        gfx.DrawString("Inactive Users", fontH2, XBrushes.Black, margin, y); y += 24;
        gfx.DrawLine(XPens.LightGray, margin, y, margin + PageWidth(gfx), y); y += 14;

        double u1 = margin, u2 = margin + 180, u3 = margin + 310;
        void DrawInactiveHeaders()
        {
            gfx.DrawString("Name",       fontB, XBrushes.Black, u1, y);
            gfx.DrawString("Last Logon", fontB, XBrushes.Black, u2, y);
            gfx.DrawString("OU",         fontB, XBrushes.Black, u3, y);
            y += 16;
        }
        DrawInactiveHeaders();

        foreach (var u in data.InactiveUsers)
        {
            if (y > gfx.PageSize.Height - 60) { gfx = NewPage(); y = 40; DrawInactiveHeaders(); }
            gfx.DrawString(u.DisplayName,                              fontS, XBrushes.Black, u1, y);
            gfx.DrawString(u.LastLogon?.ToString("yyyy-MM-dd") ?? "Never", fontS, XBrushes.Black, u2, y);
            gfx.DrawString(DnHelper.OuFromDn(u.DistinguishedName),    fontS, XBrushes.Black, u3, y);
            y += 14;
        }
        if (data.InactiveUsers.Count == 0)
            gfx.DrawString("No inactive users.", fontR, XBrushes.DarkGreen, margin, y);

        // ── 4. EXPIRING PASSWORDS ─────────────────────────────────────────────
        gfx = NewPage();
        y   = 40;
        gfx.DrawString("Expiring / Expired Passwords", fontH2, XBrushes.Black, margin, y); y += 24;
        gfx.DrawLine(XPens.LightGray, margin, y, margin + PageWidth(gfx), y); y += 14;

        double p1 = margin, p2 = margin + 180, p3 = margin + 360, p4 = margin + 480;
        void DrawPasswordExpiryHeaders()
        {
            gfx.DrawString("Name",    fontB, XBrushes.Black, p1, y);
            gfx.DrawString("Email",   fontB, XBrushes.Black, p2, y);
            gfx.DrawString("Expires", fontB, XBrushes.Black, p3, y);
            gfx.DrawString("Days",    fontB, XBrushes.Black, p4, y);
            y += 16;
        }
        DrawPasswordExpiryHeaders();

        foreach (var u in data.ExpiringPasswords)
        {
            if (y > gfx.PageSize.Height - 60) { gfx = NewPage(); y = 40; DrawPasswordExpiryHeaders(); }
            var expired  = u.DaysUntilPasswordExpiry.HasValue && u.DaysUntilPasswordExpiry.Value <= 0;
            var rb = expired ? XBrushes.DarkRed : XBrushes.Black;
            gfx.DrawString(u.DisplayName,                                  fontS, rb, p1, y);
            gfx.DrawString(u.Email,                                        fontS, rb, p2, y);
            gfx.DrawString(u.PasswordExpires?.ToString("yyyy-MM-dd") ?? "", fontS, rb, p3, y);
            gfx.DrawString(u.DaysUntilPasswordExpiry?.ToString() ?? "N/A", fontS, rb, p4, y);
            y += 14;
        }
        if (data.ExpiringPasswords.Count == 0)
            gfx.DrawString("No expiring passwords.", fontR, XBrushes.DarkGreen, margin, y);

        // ── 5. DOMAIN ADMINS ──────────────────────────────────────────────────
        gfx = NewPage();
        y   = 40;
        gfx.DrawString("Domain Admins", fontH2, XBrushes.Black, margin, y); y += 24;
        gfx.DrawLine(XPens.LightGray, margin, y, margin + PageWidth(gfx), y); y += 14;

        foreach (var admin in data.DomainAdmins)
        {
            if (y > gfx.PageSize.Height - 60) { gfx = NewPage(); y = 40; }
            gfx.DrawString($"• {admin}", fontR, XBrushes.Black, margin + 8, y);
            y += 16;
        }
        if (data.DomainAdmins.Count == 0)
            gfx.DrawString("No domain admins found.", fontR, XBrushes.Gray, margin, y);

        doc.Save(filePath);
    }

    public void ExportPasswordReport(IEnumerable<AdUser> users, string filePath)
    {
        var list  = users.ToList();
        var doc   = new PdfDocument();
        doc.Info.Title = "DirHealth — Password Expiry Report";

        var page  = doc.AddPage();
        var gfx   = XGraphics.FromPdfPage(page);
        var fontH = new XFont("Arial", 18, XFontStyleEx.Bold);
        var fontB = new XFont("Arial", 10, XFontStyleEx.Bold);
        var fontR = new XFont("Arial", 10, XFontStyleEx.Regular);

        double y      = 40;
        double margin = 40;
        double pw     = page.Width.Point - margin * 2;

        gfx.DrawString("DirHealth — Password Expiry Report", fontH, XBrushes.Black, margin, y);
        y += 24;
        gfx.DrawString(
            $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm}  |  {list.Count} user(s)",
            fontR, XBrushes.Gray, margin, y);
        y += 8;
        gfx.DrawLine(XPens.LightGray, margin, y, margin + pw, y);
        y += 16;

        double c1 = margin, c2 = margin + 180, c3 = margin + 370, c4 = margin + 460;
        void DrawPasswordHeaders()
        {
            gfx.DrawString("Name",      fontB, XBrushes.Black, c1, y);
            gfx.DrawString("Email",     fontB, XBrushes.Black, c2, y);
            gfx.DrawString("Expires",   fontB, XBrushes.Black, c3, y);
            gfx.DrawString("Days Left", fontB, XBrushes.Black, c4, y);
            y += 6;
            gfx.DrawLine(XPens.LightGray, margin, y, margin + pw, y);
            y += 12;
        }
        DrawPasswordHeaders();

        foreach (var u in list)
        {
            if (y > page.Height.Point - 60)
            {
                page = doc.AddPage();
                gfx  = XGraphics.FromPdfPage(page);
                y    = 40;
                DrawPasswordHeaders();
            }
            var expired  = u.DaysUntilPasswordExpiry.HasValue && u.DaysUntilPasswordExpiry.Value <= 0;
            var rowBrush = expired ? XBrushes.DarkRed : XBrushes.Black;
            gfx.DrawString(u.DisplayName,                                     fontR, rowBrush, c1, y);
            gfx.DrawString(u.Email,                                           fontR, rowBrush, c2, y);
            gfx.DrawString(u.PasswordExpires?.ToString("yyyy-MM-dd") ?? "",   fontR, rowBrush, c3, y);
            gfx.DrawString(u.DaysUntilPasswordExpiry?.ToString() ?? "N/A",    fontR, rowBrush, c4, y);
            y += 16;
        }

        if (list.Count == 0)
            gfx.DrawString("No users with expiring passwords.", fontR, XBrushes.DarkGreen, margin, y);

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
