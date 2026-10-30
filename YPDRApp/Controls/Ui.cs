using System.Windows.Media;

namespace YoutubePlaylistDownloader.Controls;

/// <summary>
/// Attached properties shared by the control templates in Themes/Controls.xaml.
/// </summary>
public static class Ui
{
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.RegisterAttached("CornerRadius", typeof(CornerRadius), typeof(Ui), new PropertyMetadata(new CornerRadius(0)));

    public static CornerRadius GetCornerRadius(DependencyObject obj) => (CornerRadius)obj.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject obj, CornerRadius value) => obj.SetValue(CornerRadiusProperty, value);

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(Geometry), typeof(Ui), new PropertyMetadata(null));

    public static Geometry GetIcon(DependencyObject obj) => (Geometry)obj.GetValue(IconProperty);
    public static void SetIcon(DependencyObject obj, Geometry value) => obj.SetValue(IconProperty, value);

    public static readonly DependencyProperty IconSizeProperty =
        DependencyProperty.RegisterAttached("IconSize", typeof(double), typeof(Ui), new PropertyMetadata(20.0));

    public static double GetIconSize(DependencyObject obj) => (double)obj.GetValue(IconSizeProperty);
    public static void SetIconSize(DependencyObject obj, double value) => obj.SetValue(IconSizeProperty, value);

    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.RegisterAttached("HoverBackground", typeof(Brush), typeof(Ui), new PropertyMetadata(null));

    public static Brush GetHoverBackground(DependencyObject obj) => (Brush)obj.GetValue(HoverBackgroundProperty);
    public static void SetHoverBackground(DependencyObject obj, Brush value) => obj.SetValue(HoverBackgroundProperty, value);

    public static readonly DependencyProperty HoverForegroundProperty =
        DependencyProperty.RegisterAttached("HoverForeground", typeof(Brush), typeof(Ui), new PropertyMetadata(null));

    public static Brush GetHoverForeground(DependencyObject obj) => (Brush)obj.GetValue(HoverForegroundProperty);
    public static void SetHoverForeground(DependencyObject obj, Brush value) => obj.SetValue(HoverForegroundProperty, value);

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Ui), new PropertyMetadata(null));

    public static string GetPlaceholder(DependencyObject obj) => (string)obj.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject obj, string value) => obj.SetValue(PlaceholderProperty, value);

    public static readonly DependencyProperty BadgeProperty =
        DependencyProperty.RegisterAttached("Badge", typeof(string), typeof(Ui), new PropertyMetadata(null));

    public static string GetBadge(DependencyObject obj) => (string)obj.GetValue(BadgeProperty);
    public static void SetBadge(DependencyObject obj, string value) => obj.SetValue(BadgeProperty, value);

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.RegisterAttached("Progress", typeof(double), typeof(Ui), new PropertyMetadata(0.0));

    public static double GetProgress(DependencyObject obj) => (double)obj.GetValue(ProgressProperty);
    public static void SetProgress(DependencyObject obj, double value) => obj.SetValue(ProgressProperty, value);
}
