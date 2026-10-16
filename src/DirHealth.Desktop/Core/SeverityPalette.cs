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
}
