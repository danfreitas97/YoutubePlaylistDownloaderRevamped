using System.Globalization;
using System.Windows.Media;

namespace YoutubePlaylistDownloader.Controls;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value switch
        {
            bool b => b,
            int i => i != 0,
            string s => !string.IsNullOrWhiteSpace(s),
            null => false,
            _ => true
        };
        return flag ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is Visibility v && v == Visibility.Visible) ^ Invert;
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>
/// True when the bound value equals the converter parameter; used to drive radio-style choices from a string/enum property.
/// </summary>
public class EqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true)
            return Binding.DoNothing;

        if (targetType.IsEnum)
            return Enum.Parse(targetType, parameter.ToString());

        return parameter;
    }
}

/// <summary>
/// Looks up an image resource (the language flags) by key: "Flag." + value.
/// </summary>
public class FlagConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value == null ? null : Application.Current.TryFindResource($"Flag.{value}") as ImageSource;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Turns a 0-100 progress into a dash pattern that draws that share of the rail item's pill outline.
/// </summary>
public class ProgressToDashArrayConverter : IValueConverter
{
    // Perimeter of the 54x30 pill (radius 15) in units of its 2px stroke.
    private const double Perimeter = (2 * (54 - 30) + 2 * Math.PI * 15) / 2;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var progress = value is double d ? Math.Clamp(d, 0, 100) : 0;
        return new DoubleCollection { Perimeter * progress / 100, Perimeter * 2 };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Width of a progress fill: values[0] = percent (0-100), values[1] = available width.
/// </summary>
public class PercentWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[0] is not double percent || values[1] is not double width)
            return 0.0;

        return Math.Max(0, width * Math.Clamp(percent, 0, 100) / 100);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Loads a remote thumbnail decoded at a small size (ConverterParameter = decode width in pixels).
/// </summary>
public class ImageUrlConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(url, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (int.TryParse(parameter?.ToString(), out var width) && width > 0)
                image.DecodePixelWidth = width;
            image.EndInit();
            return image;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Looks up an application resource (an icon geometry, for example) by the bound key.
/// </summary>
public class ResourceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value == null ? null : Application.Current.TryFindResource(value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Resolves a localization key into the current language's string.
/// </summary>
public class LocalizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value == null ? string.Empty : Loc.Get(value.ToString());

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
