using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DirHealth.Desktop;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}

public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NonZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class IsNegativeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i && i < 0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class EqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == parameter?.ToString();

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? System.Convert.ChangeType(parameter, typeof(int)) : System.Windows.DependencyProperty.UnsetValue;
}

public class ScoreColorConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush Green;
    private static readonly System.Windows.Media.SolidColorBrush Amber;
    private static readonly System.Windows.Media.SolidColorBrush Red;

    static ScoreColorConverter()
    {
        Green = new(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E)); Green.Freeze();
        Amber = new(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B)); Amber.Freeze();
        Red   = new(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)); Red.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int score) return System.Windows.Media.Brushes.White;
        if (score >= 80) return Green;
        if (score >= 60) return Amber;
        return Red;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class RelativeScanTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string s || s == "Never") return s ?? "Never";
        if (!DateTime.TryParseExact(s, "dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dt)) return s;
        var diff = DateTime.Now - dt;
        if (diff.TotalMinutes < 1)  return "just now";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} min ago";
        if (diff.TotalHours   < 24) return $"{(int)diff.TotalHours} h ago";
        if (diff.TotalDays    <  2) return "yesterday";
        return $"{(int)diff.TotalDays} days ago";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DaysToExpiryColorConverter : IValueConverter
{
    private static readonly System.Windows.Media.SolidColorBrush Red;
    private static readonly System.Windows.Media.SolidColorBrush Orange;
    private static readonly System.Windows.Media.SolidColorBrush Yellow;
    private static readonly System.Windows.Media.SolidColorBrush Green;

    static DaysToExpiryColorConverter()
    {
        Red    = new(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44)); Red.Freeze();
        Orange = new(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B)); Orange.Freeze();
        Yellow = new(System.Windows.Media.Color.FromRgb(0xFB, 0xBF, 0x24)); Yellow.Freeze();
        Green  = new(System.Windows.Media.Color.FromRgb(0x22, 0xC5, 0x5E)); Green.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int days) return System.Windows.Media.Brushes.Transparent;
        if (days < 14) return Red;
        if (days < 30) return Orange;
        if (days < 60) return Yellow;
        return Green;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
