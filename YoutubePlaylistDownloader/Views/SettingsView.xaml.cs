namespace YoutubePlaylistDownloader.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void Toc_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: FrameworkElement target })
            ScrollTo(target);
    }

    private void ScrollTo(FrameworkElement target)
    {
        // Scroll so the section title sits at the top of the viewport.
        var offset = target.TransformToAncestor((System.Windows.Media.Visual)Scroller.Content).Transform(new System.Windows.Point(0, 0)).Y;
        Scroller.ScrollToVerticalOffset(Math.Max(0, offset - 8));
    }
}
