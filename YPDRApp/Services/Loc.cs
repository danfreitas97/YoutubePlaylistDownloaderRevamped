namespace YoutubePlaylistDownloader.Services;

/// <summary>
/// Access to the localized strings in Languages/*.xaml and switching between languages at runtime.
/// English is always merged first so any key missing from a translation falls back to it.
/// </summary>
public static class Loc
{
    private const string LanguagesFolder = "/Languages/";
    private static ResourceDictionary current;

    public static event EventHandler LanguageChanged;

    public static IReadOnlyList<string> Languages { get; private set; } = [];

    public static string Current => GlobalConsts.settings.Language;

    public static string Get(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] args)
    {
        try
        {
            return string.Format(Get(key), args);
        }
        catch (FormatException)
        {
            return Get(key);
        }
    }

    public static FlowDirection FlowDirection =>
        Application.Current?.TryFindResource("FlowDirection") is FlowDirection direction ? direction : FlowDirection.LeftToRight;

    public static void Initialize()
    {
        Languages = (Get("LanguageList") ?? "English").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var language = GlobalConsts.settings.Language;
        if (string.IsNullOrWhiteSpace(language) || !Languages.Contains(language))
            language = GlobalConsts.DefaultLanguage;

        Apply(language, raise: false);
    }

    public static void Apply(string language) => Apply(language, raise: true);

    private static void Apply(string language, bool raise)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;

        if (current != null)
        {
            dictionaries.Remove(current);
            current = null;
        }

        if (language != "English")
        {
            try
            {
                current = new ResourceDictionary { Source = new Uri($"{LanguagesFolder}{language}.xaml", UriKind.Relative) };
                dictionaries.Add(current);
            }
            catch (Exception ex)
            {
                GlobalConsts.Log(ex.ToString(), "Loc.Apply").ConfigureAwait(false);
                language = "English";
            }
        }

        GlobalConsts.settings.Language = language;

        if (raise)
        {
            GlobalConsts.SaveSettings();
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
