using System.Windows;

namespace DirHealth.Desktop.Core.Theme;

public static class ThemeManager
{
    public static string CurrentTheme => "dark";

    public static void LoadSavedTheme() => Apply("dark");

    public static void Apply(string _)
    {
        var dict     = Application.Current.Resources.MergedDictionaries;
        var existing = dict.FirstOrDefault(d =>
            d.Source?.OriginalString.Contains("/Themes/") == true);
        if (existing is not null) dict.Remove(existing);

        dict.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Resources/Themes/Dark.xaml")
        });
    }
}
