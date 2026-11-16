using System.IO;
using System.Windows;
using DirHealth.Desktop.Core.AD.Models;
using PdfSharp.Drawing;

namespace DirHealth.Desktop.Core.Export;

/// <summary>
/// The design system shared by every DirHealth PDF (dashboard report, findings tab, password report, user
/// dossier): colours, the type scale, the logo and small drawing primitives (pills, tiles, the score ring).
/// Page flow (header, footer "Page x of y", breaks, tables, code blocks) is <see cref="Reports.PdfFlow"/>.
/// Severity colours come only from <see cref="SeverityPalette"/>; tints and dark text shades are derived
/// from it here, never typed as separate hex values (A9).
/// </summary>
internal sealed class PdfPageBuilder
{
    // ── Colours (neutral slate scale + one brand blue) ───────────────────────
    internal static readonly XColor ColNavy        = XColor.FromArgb( 15,  23,  42); // #0f172a  cover, brand
    internal static readonly XColor ColNavy2       = XColor.FromArgb( 30,  41,  59); // #1e293b  cover panels
    internal static readonly XColor ColNavy3       = XColor.FromArgb( 51,  65,  85); // #334155  ring track
    internal static readonly XColor ColAccent      = XColor.FromArgb( 37,  99, 235); // #2563eb  links, accents
    internal static readonly XColor ColAccentSoft  = XColor.FromArgb(219, 234, 254); // #dbeafe
    internal static readonly XColor ColOnDarkMuted = XColor.FromArgb(148, 163, 184); // #94a3b8
    internal static readonly XColor ColOnDark      = XColor.FromArgb(226, 232, 240); // #e2e8f0
    internal static readonly XColor ColScoreGood   = XColor.FromArgb(125, 255, 179); // #7DFFB3 (on dark)
    internal static readonly XColor ColScoreWarn   = XColor.FromArgb(252, 211,  77); // #FCD34D (on dark)
    internal static readonly XColor ColScoreCrit   = XColor.FromArgb(252, 165, 165); // #FCA5A5 (on dark)
    // Severity accents: single source shared with the app (A9, S14-6)
    internal static readonly XColor ColCritical    = FromRgb(SeverityPalette.CriticalRgb);
    internal static readonly XColor ColHigh        = FromRgb(SeverityPalette.HighRgb);
    internal static readonly XColor ColMedium      = FromRgb(SeverityPalette.MediumRgb);
    internal static readonly XColor ColLow         = FromRgb(SeverityPalette.LowRgb);

    internal static readonly XColor ColBodyText    = XColor.FromArgb( 15,  23,  42); // #0f172a
    internal static readonly XColor ColText2       = XColor.FromArgb( 51,  65,  85); // #334155  secondary body
    internal static readonly XColor ColLabelGray   = XColor.FromArgb(100, 116, 139); // #64748b  labels, captions
    internal static readonly XColor ColBorder      = XColor.FromArgb(226, 232, 240); // #e2e8f0  hairlines
    internal static readonly XColor ColBorderDark  = XColor.FromArgb(203, 213, 225); // #cbd5e1
    internal static readonly XColor ColSurface     = XColor.FromArgb(248, 250, 252); // #f8fafc  light bands
    internal static readonly XColor ColSurface2    = XColor.FromArgb(241, 245, 249); // #f1f5f9  table heads, code

    private static XColor FromRgb((byte R, byte G, byte B) c) => XColor.FromArgb(c.R, c.G, c.B);

    /// <summary>Mixes a colour with white (0 = colour, 1 = white) — light tints of the palette colours.</summary>
    internal static XColor Tint(XColor c, double white) => XColor.FromArgb(
        (int)Math.Round(c.R + (255 - c.R) * white), (int)Math.Round(c.G + (255 - c.G) * white), (int)Math.Round(c.B + (255 - c.B) * white));

    /// <summary>Darker shade of a colour (1 = unchanged) — readable text in the palette's hue on white.</summary>
    internal static XColor Shade(XColor c, double f) => XColor.FromArgb((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));

    // ── Type scale: few sizes, used the same way in every report ──────────────
    internal const string Sans = "Segoe UI";
    internal const string MonoFamily = "Consolas";
    internal static readonly XFont F7B   = new(Sans,  7.5, XFontStyleEx.Bold);     // table heads, overlines, pills
    internal static readonly XFont F7    = new(Sans,  7.5, XFontStyleEx.Regular);  // footer, captions
    internal static readonly XFont F8    = new(Sans,  8.5, XFontStyleEx.Regular);  // table cells, small text
    internal static readonly XFont F8B   = new(Sans,  8.5, XFontStyleEx.Bold);
    internal static readonly XFont F9    = new(Sans,  9.5, XFontStyleEx.Regular);  // body
    internal static readonly XFont F9B   = new(Sans,  9.5, XFontStyleEx.Bold);
    internal static readonly XFont F10   = new(Sans, 10.5, XFontStyleEx.Regular);  // lead text
    internal static readonly XFont F12B  = new(Sans, 12,   XFontStyleEx.Bold);     // card titles
    internal static readonly XFont F15B  = new(Sans, 15,   XFontStyleEx.Bold);     // section headings
    internal static readonly XFont F22B  = new(Sans, 22,   XFontStyleEx.Bold);     // tile numbers
    internal static readonly XFont F26B  = new(Sans, 26,   XFontStyleEx.Bold);     // cover title
    internal static readonly XFont F36B  = new(Sans, 36,   XFontStyleEx.Bold);     // score
    internal static readonly XFont Mono8 = new(MonoFamily, 8, XFontStyleEx.Regular);

    // ── Layout ────────────────────────────────────────────────────────────────
    internal const double Margin = 46;

    private readonly XImage? _logo;

    internal PdfPageBuilder()
    {
        try
        {
            var sri = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/icon_128.png"));
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

    internal void DrawLogo(XGraphics g, double x, double y, double size)
    {
        if (_logo != null) g.DrawImage(_logo, x, y, size, size);
        else g.DrawRoundedRectangle(new XSolidBrush(ColAccent), x, y, size, size, size / 4, size / 4);
    }

    /// <summary>Severity row tint, accent (= palette colour) and a dark text brush in the same hue.</summary>
    internal (XColor bg, XColor border, XSolidBrush text) SeverityStyle(FindingSeverity sev)
    {
        var accent = sev switch
        {
            FindingSeverity.Critical => ColCritical,
            FindingSeverity.High     => ColHigh,
            FindingSeverity.Medium   => ColMedium,
            _                        => ColLow,
        };
        return (Tint(accent, 0.92), accent, new XSolidBrush(Shade(accent, sev == FindingSeverity.Critical ? 0.78 : 0.55)));
    }

    internal static FindingSeverity ParseSeverity(string s) =>
        Enum.TryParse<FindingSeverity>(s, true, out var v) ? v : FindingSeverity.Low;

    /// <summary>Cover score colours (bright, for the dark cover). Same thresholds as the report's score label.</summary>
    internal XColor ScoreColor(int score) =>
        score >= 80 ? ColScoreGood :
        score >= 60 ? ColScoreWarn :
                      ColScoreCrit;

    /// <summary>A rounded label ("CRITICAL", "TIER 0"). Returns its width. <paramref name="baseline"/> is the text baseline.</summary>
    internal static double Pill(XGraphics g, string text, XFont font, XColor fill, XColor textColor, double x, double baseline,
                                XColor? outline = null, Action<string>? sink = null)
    {
        double w = g.MeasureString(text, font).Width + 12, h = font.Size + 6;
        double top = baseline - font.Size - 1.5;
        if (outline is { } o) g.DrawRoundedRectangle(new XPen(o, 0.8), new XSolidBrush(fill), x, top, w, h, h, h);
        else g.DrawRoundedRectangle(new XSolidBrush(fill), x, top, w, h, h, h);
        sink?.Invoke(text);
        g.DrawString(text, font, new XSolidBrush(textColor), x + 6, baseline);
        return w;
    }

    /// <summary>Severity pill: palette colour with white text — the word carries the meaning in greyscale too.</summary>
    internal double SeverityPill(XGraphics g, FindingSeverity sev, double x, double baseline, Action<string>? sink = null) =>
        Pill(g, sev.ToString().ToUpperInvariant(), F7B, SeverityStyle(sev).border, XColors.White, x, baseline, sink: sink);

    /// <summary>
    /// KPI tile: tinted box, 3 pt accent bar on top, big number, label below. Returns nothing; the caller
    /// advances its cursor by <paramref name="h"/>.
    /// </summary>
    internal static void Tile(XGraphics g, double x, double y, double w, double h, string value, string label,
                              XColor accent, XColor tint, XColor valueColor, Action<string>? sink = null)
    {
        g.DrawRectangle(new XSolidBrush(tint), x, y, w, h);
        g.DrawRectangle(new XSolidBrush(accent), x, y, w, 3);
        sink?.Invoke(value);
        g.DrawString(value, F22B, new XSolidBrush(valueColor), x + 12, y + 33);
        sink?.Invoke(label);
        g.DrawString(label, F8B, new XSolidBrush(ColText2), x + 12, y + h - 12);
    }

    /// <summary>Score ring: a full track and an arc proportional to the score (none when not available).</summary>
    internal static void Ring(XGraphics g, double cx, double cy, double r, double thickness, double fraction, XColor track, XColor arc)
    {
        g.DrawEllipse(new XPen(track, thickness), cx - r, cy - r, 2 * r, 2 * r);
        if (fraction <= 0) return;
        var pen = new XPen(arc, thickness) { LineCap = XLineCap.Round };
        g.DrawArc(pen, cx - r, cy - r, 2 * r, 2 * r, -90, Math.Min(359.9, 360 * fraction));
    }
}
