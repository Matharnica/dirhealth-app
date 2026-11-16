using DirHealth.Desktop.Core.AD.Models;
using System.Windows.Media;

namespace DirHealth.Desktop.Core;

/// <summary>
/// Single source for severity accent colours (A9, S14-6). The app (Styles.xaml via
/// x:Static) and the PDF exporter both read from here, so a finding has the same colour on
/// screen and on paper. Values chosen to stay readable on the dark UI and on white paper.
/// Dark-theme badge tints and PDF row tints are derived backgrounds, not accents.
/// </summary>
public static class SeverityPalette
{
    public static readonly (byte R, byte G, byte B) CriticalRgb = (220,  38,  38); // #DC2626
    public static readonly (byte R, byte G, byte B) HighRgb     = (217, 119,   6); // #D97706
    public static readonly (byte R, byte G, byte B) MediumRgb   = (202, 138,   4); // #CA8A04
    public static readonly (byte R, byte G, byte B) LowRgb      = (107, 114, 128); // #6B7280

    public static (byte R, byte G, byte B) Rgb(FindingSeverity s) => s switch
    {
        FindingSeverity.Critical => CriticalRgb,
        FindingSeverity.High     => HighRgb,
        FindingSeverity.Medium   => MediumRgb,
        _                        => LowRgb,
    };

    public static string Hex(FindingSeverity s)
    {
        var (r, g, b) = Rgb(s);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    // WPF colours for Styles.xaml ({x:Static core:SeverityPalette.HighColor})
    public static Color CriticalColor => Color.FromRgb(CriticalRgb.R, CriticalRgb.G, CriticalRgb.B);
    public static Color HighColor     => Color.FromRgb(HighRgb.R,     HighRgb.G,     HighRgb.B);
    public static Color MediumColor   => Color.FromRgb(MediumRgb.R,   MediumRgb.G,   MediumRgb.B);
    public static Color LowColor      => Color.FromRgb(LowRgb.R,      LowRgb.G,      LowRgb.B);

    // ── Dark-theme severity badge: dark tint behind light text (screen only, not the PDF) ──
    // Every pair has at least 4.5:1 contrast (WCAG AA) and the four tints differ, so High and
    // Medium no longer share one colour. Checked by UiPolishTests.SeverityBadge_*.
    //   Critical #FECACA on #7F1D1D = 6.9:1 · High #FED7AA on #7C2D12 = 6.9:1
    //   Medium   #FEF08A on #713F12 = 7.5:1 · Low  #E2E8F0 on #334155 = 8.4:1
    public static readonly (byte R, byte G, byte B) CriticalBadgeBgRgb   = (127,  29,  29); // #7F1D1D
    public static readonly (byte R, byte G, byte B) HighBadgeBgRgb       = (124,  45,  18); // #7C2D12
    public static readonly (byte R, byte G, byte B) MediumBadgeBgRgb     = (113,  63,  18); // #713F12
    public static readonly (byte R, byte G, byte B) LowBadgeBgRgb        = ( 51,  65,  85); // #334155
    public static readonly (byte R, byte G, byte B) CriticalBadgeTextRgb = (254, 202, 202); // #FECACA
    public static readonly (byte R, byte G, byte B) HighBadgeTextRgb     = (254, 215, 170); // #FED7AA
    public static readonly (byte R, byte G, byte B) MediumBadgeTextRgb   = (254, 240, 138); // #FEF08A
    public static readonly (byte R, byte G, byte B) LowBadgeTextRgb      = (226, 232, 240); // #E2E8F0

    public static (byte R, byte G, byte B) BadgeBackgroundRgb(FindingSeverity s) => s switch
    {
        FindingSeverity.Critical => CriticalBadgeBgRgb,
        FindingSeverity.High     => HighBadgeBgRgb,
        FindingSeverity.Medium   => MediumBadgeBgRgb,
        _                        => LowBadgeBgRgb,
    };

    public static (byte R, byte G, byte B) BadgeTextRgb(FindingSeverity s) => s switch
    {
        FindingSeverity.Critical => CriticalBadgeTextRgb,
        FindingSeverity.High     => HighBadgeTextRgb,
        FindingSeverity.Medium   => MediumBadgeTextRgb,
        _                        => LowBadgeTextRgb,
    };

    private static Color C((byte R, byte G, byte B) c) => Color.FromRgb(c.R, c.G, c.B);
    public static Color CriticalBadgeBackground => C(CriticalBadgeBgRgb);
    public static Color HighBadgeBackground     => C(HighBadgeBgRgb);
    public static Color MediumBadgeBackground   => C(MediumBadgeBgRgb);
    public static Color LowBadgeBackground      => C(LowBadgeBgRgb);
    public static Color CriticalBadgeText       => C(CriticalBadgeTextRgb);
    public static Color HighBadgeText           => C(HighBadgeTextRgb);
    public static Color MediumBadgeText         => C(MediumBadgeTextRgb);
    public static Color LowBadgeText            => C(LowBadgeTextRgb);
}
