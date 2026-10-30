using Microsoft.Win32;
using System.Windows;
using ShotX.Models;

namespace ShotX.Services;

public static class ThemeService
{
    public static event Action? SystemThemeChanged;

    private static bool _listening;

    public static void StartListeningForSystemThemeChanges()
    {
        if (_listening) return;
        _listening = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General) SystemThemeChanged?.Invoke();
        };
    }

    public static bool IsSystemInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch
        {
            return false;
        }
    }

    public static void Apply(AppTheme theme)
    {
        var resolvedDark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemInDarkMode()
        };

        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var colorDictUri = resolvedDark ? "Themes/Dark.xaml" : "Themes/Light.xaml";
        var newDict = new ResourceDictionary { Source = new Uri(colorDictUri, UriKind.Relative) };

        // Color dictionary is always merged first (index 0); Styles.xaml stays in place.
        if (dictionaries.Count > 0) dictionaries[0] = newDict;
        else dictionaries.Add(newDict);
    }
}
