using System.Globalization;

namespace DirHealth.Desktop.Core.Export.Reports;

/// <summary>
/// Wording helpers for every PDF (dashboard report, findings tab, password report, user dossier): real
/// plurals instead of "finding(s)", and one culture-invariant date format. Pure — tested without a PDF.
/// </summary>
public static class ReportText
{
    /// <summary>The one date-time format of the PDFs (ISO, local time; the time zone is printed once per report).</summary>
    public const string DateTimeFormat = "yyyy-MM-dd HH:mm";
    public const string DateFormat     = "yyyy-MM-dd";

    /// <summary>"1 failed check", "13 open findings", "1,842 users". <paramref name="plural"/> defaults to noun + "s".</summary>
    public static string Count(int n, string noun, string? plural = null) =>
        $"{n.ToString("N0", CultureInfo.InvariantCulture)} {(n == 1 ? noun : plural ?? noun + "s")}";

    /// <summary>The noun alone, matching <paramref name="n"/>: Noun(1, "is", "are") → "is".</summary>
    public static string Noun(int n, string singular, string plural) => n == 1 ? singular : plural;

    public static string Time(DateTime local) => local.ToString(DateTimeFormat, CultureInfo.InvariantCulture);

    /// <summary>"UTC+01:00" — the offset of a local time, printed once per report.</summary>
    public static string Zone(DateTime local)
    {
        var off = TimeZoneInfo.Local.GetUtcOffset(local);
        return $"UTC{(off < TimeSpan.Zero ? "-" : "+")}{off:hh\\:mm}";
    }

    /// <summary>
    /// The app's scan-time display string ("16.11.2026 13:52:10", <see cref="ScanJsonExporter.DisplayScanTimeFormat"/>)
    /// in the report format ("2026-11-16 13:52"). Anything else ("Never", an old cache value) stays as it is.
    /// </summary>
    /// <param name="toUtc">Converts the local display time to UTC (the user dossier prints all its times in UTC).</param>
    public static string ScanTime(string? display, bool toUtc = false)
    {
        if (string.IsNullOrWhiteSpace(display)) return display ?? "";
        return DateTime.TryParseExact(display.Trim(), ScanJsonExporter.DisplayScanTimeFormat, CultureInfo.InvariantCulture,
                                      DateTimeStyles.AssumeLocal, out var t)
            ? Time(toUtc ? t.ToUniversalTime() : t) : display;
    }
}
