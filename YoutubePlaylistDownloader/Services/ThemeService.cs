using Microsoft.Win32;

namespace YoutubePlaylistDownloader.Services;

/// <summary>
/// Swaps the Dark/Light brush dictionaries. "System" follows the Windows app theme and updates when it changes.
/// </summary>
public static class ThemeService
{
    public const string System = "System";
    public const string Light = "Light";
    public const string Dark = "Dark";

    private static ResourceDictionary current;

    public static event EventHandler ThemeChanged;

    /// <summary>The user's choice: System, Light or Dark.</summary>
    public static string Mode => GlobalConsts.settings.Theme is Light or Dark ? GlobalConsts.settings.Theme : System;

    /// <summary>What is actually on screen: Light or Dark.</summary>
    public static bool IsDark { get; private set; } = true;

    public static void Initialize()
    {
        SystemEvents.UserPreferenceChanged += (s, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Mode == System)
                Application.Current?.Dispatcher.Invoke(() => Apply(System));
        };

        Apply(Mode, save: false);
    }

    public static void Apply(string mode) => Apply(mode, save: true);

    public static void Toggle() => Apply(IsDark ? Light : Dark);

    private static void Apply(string mode, bool save)
    {
        var dark = mode switch
        {
            Light => false,
            Dark => true,
            _ => SystemUsesDarkTheme()
        };

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var next = new ResourceDictionary { Source = new Uri(dark ? "/Themes/Dark.xaml" : "/Themes/Light.xaml", UriKind.Relative) };

        // Theme brushes go first so the control styles merged after them can resolve them.
        if (current != null)
        {
            var index = dictionaries.IndexOf(current);
            dictionaries[index] = next;
        }
        else
        {
            dictionaries.Insert(0, next);
        }

        current = next;
        IsDark = dark;
        GlobalConsts.settings.Theme = mode is Light or Dark ? mode : System;

        if (save)
            GlobalConsts.SaveSettings();

        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return true;
        }
    }
}
