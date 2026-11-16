using System.Globalization;
using DirHealth.Desktop.Core.AD.Models;
using DirHealth.Desktop.Core.Export.Reports;
using PdfSharp.Drawing;
using UI = DirHealth.Desktop.Core.Export.PdfPageBuilder;

namespace DirHealth.Desktop.Core.Export;

/// <summary>
/// Password report and user dossier PDFs. Both use the shared design (<see cref="PdfPageBuilder"/>) and page
/// flow (<see cref="PdfFlow"/>) of the dashboard report: running header, title band, KPI tiles, wrapped
/// tables with repeated column headers, footer with "Page x of y".
/// </summary>
public class PdfExporter
{
    // S21-4: the findings-tab PDF goes through ReportBuilder → ReportPdfRenderer like the dashboard
    // report — the separate ExportReport layout was removed (one PDF engine for findings).

    /// <summary>Days left at or below which a password counts as "expires soon" (red/orange like the app's converter).</summary>
    private const int SoonDays = 14;

    public void ExportPasswordReport(IEnumerable<AdUser> users, string filePath)
    {
        var list = users.ToList();
        var now  = DateTime.Now;
        bool Expired(AdUser u) => u.DaysUntilPasswordExpiry is <= 0;
        bool Soon(AdUser u)    => u.DaysUntilPasswordExpiry is > 0 and <= SoonDays;
        bool Month(AdUser u)   => u.DaysUntilPasswordExpiry is > SoonDays and <= 30;
        string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);

        var f = new PdfFlow("Password Expiry Report", ReportBuilder.ClassificationText, null);
        f.NewPage();
        f.Hero("PASSWORD EXPIRY REPORT", "Password expiry",
               $"{N(list.Count)} account(s) with an expiring password · created {now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
        f.Tiles(
        [
            (N(list.Count(Expired)), "Expired",                  UI.ColCritical),
            (N(list.Count(Soon)),    $"Expire within {SoonDays} days", UI.ColHigh),
            (N(list.Count(Month)),   $"Expire in {SoonDays + 1}–30 days", UI.ColMedium),
            (N(list.Count),          "Accounts listed",          UI.ColAccent),
        ]);

        f.Heading("Accounts by expiry date", 60);
        var rows = list.Select(u => (IReadOnlyList<string>)new[]
        {
            u.DisplayName.Length > 0 ? u.DisplayName : u.SamAccountName,
            u.Email ?? "",
            u.PasswordExpires?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
            u.DaysUntilPasswordExpiry?.ToString(CultureInfo.InvariantCulture) ?? "N/A",
            Expired(u) ? "Expired" : Soon(u) ? "Soon" : "OK",
        }).ToList();
        f.Table(new ReportTable(["Name", "Email", "Expires", "Days left", "Status"], rows, 0, "", Weights: [0.30, 0.38, 0.12, 0.09, 0.11]),
            [CellKind.Text, CellKind.Muted, CellKind.Mono, CellKind.Number, CellKind.Text],
            "No users with expiring passwords.",
            i => Expired(list[i]) ? UI.ColCritical : Soon(list[i]) ? UI.ColHigh : null);

        f.Finish(filePath, "DirHealth — Password Expiry Report",
            $"DirHealth {ExportMetadata.AppVersion} · Password Expiry Report · created {now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}");
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
        var f = new PdfFlow("User Dossier", $"{d.Context.Domain} · {ReportBuilder.ClassificationText}", textSink);
        var crit = new XSolidBrush(UI.ColCritical);
        f.NewPage();
        f.Hero("USER DOSSIER", d.User.DisplayName.Length > 0 ? d.User.DisplayName : d.User.SamAccountName,
               $"{d.User.SamAccountName} · {d.Context.Domain}",
               d.IsTier0 ? new List<(string, XColor)> { ("TIER 0 MEMBER", UI.ColCritical) } : null);
        f.Tiles(
        [
            (d.Groups.Count.ToString(CultureInfo.InvariantCulture), "Group memberships", UI.ColAccent),
            (d.Groups.Count(g => g.IsTier0).ToString(CultureInfo.InvariantCulture), "Tier 0 groups", UI.ColCritical),
            (d.Findings.Count.ToString(CultureInfo.InvariantCulture), "Findings affecting this user", UI.ColHigh),
        ]);

        f.Heading("ATTRIBUTES", 60, UI.F12B);
        f.KeyValues(d.Attributes.Select(r => (r.Label, r.Value, r.Warn)), 170);

        f.Heading($"GROUP MEMBERSHIPS ({d.Groups.Count})", 50, UI.F12B);
        if (d.Groups.Count == 0) f.Paragraph("No group memberships.", UI.F9, PdfFlow.Muted);
        foreach (var g in d.Groups)
        {
            var path = f.Wrap(g.Path, UI.Mono8, f.Width - 12);
            f.Ensure(16 + path.Count * 11 + 6);
            var tag = g.KindLabel + (g.IsTier0 ? " · TIER 0" : "");
            f.Text(f.Fit(g.Name, UI.F9B, f.Width - 110), UI.F9B, g.IsTier0 ? crit : PdfFlow.Ink, PdfFlow.Margin, f.Y + 11);
            f.Text(tag, UI.F7B, g.IsTier0 ? crit : PdfFlow.Muted, PdfFlow.Margin + f.Width - f.W(tag, UI.F7B), f.Y + 11);
            f.Y += 16;
            foreach (var line in path) { f.Text(line, UI.Mono8, PdfFlow.Muted, PdfFlow.Margin + 12, f.Y + 8); f.Y += 11; }
            f.Y += 5;
            f.G.DrawLine(new XPen(UI.ColBorder, 0.6), PdfFlow.Margin, f.Y, PdfFlow.Margin + f.Width, f.Y);
            f.Y += 5;
        }

        f.Heading($"AFFECTED BY FINDINGS ({d.Findings.Count})", 50, UI.F12B);
        if (d.Findings.Count == 0) f.Paragraph("This user appears in no finding of the last scan.", UI.F9, PdfFlow.Muted);
        foreach (var finding in d.Findings)
        {
            f.Ensure(34);
            var sevText = finding.Severity.ToString().ToUpperInvariant();
            // the pill and the title read as one line "HIGH  Title" — the sink gets exactly that line
            f.Sink($"{sevText}  {finding.Title}");
            double x = PdfFlow.Margin + f.Ui.SeverityPill(f.G, finding.Severity, PdfFlow.Margin, f.Y + 12) + 8;
            var title = f.Fit(finding.Title, UI.F9B, f.Width - (x - PdfFlow.Margin));
            f.G.DrawString(title, UI.F9B, f.Ui.SeverityStyle(finding.Severity).text, x, f.Y + 12);
            f.Y += 19;
            if (!string.IsNullOrEmpty(finding.Remediation))
                f.Paragraph("→ Fix: " + finding.Remediation, UI.F8, PdfFlow.Ink2, 0, 12);
            f.Y += 4;
            f.G.DrawLine(new XPen(UI.ColBorder, 0.6), PdfFlow.Margin, f.Y, PdfFlow.Margin + f.Width, f.Y);
            f.Y += 6;
        }

        f.Heading("REPORT METADATA", 60, UI.F12B);
        f.KeyValues(new[]
        {
            ("Domain",            d.Context.Domain),
            ("Domain controller", d.Context.DomainController),
            ("Run by",            d.Context.User),
            ("Last scan",         d.ScanTime),
            ("Created (UTC)",     d.CreatedUtc.ToString("yyyy-MM-dd HH:mm:ss'Z'", CultureInfo.InvariantCulture)),
            ("Tool",              $"DirHealth {d.Version}"),
            ("Integrity",         "SHA-256 of this file: see the .meta.txt next to it"),
        }.Select(r => (r.Item1, r.Item2, false)), 170);

        f.Finish(filePath, $"DirHealth — User dossier: {d.User.SamAccountName}",
            $"DirHealth {d.Version} · {d.Context.Domain} · User dossier · created {d.CreatedUtc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC");
    }
}
