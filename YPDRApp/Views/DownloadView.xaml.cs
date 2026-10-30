namespace YoutubePlaylistDownloader.Views;

public partial class DownloadView : UserControl
{
    public DownloadView()
    {
        InitializeComponent();
        IsVisibleChanged += (s, e) =>
        {
            if (IsVisible && ViewModel?.IsEmpty == true)
                Dispatcher.BeginInvoke(() => LinkBox.Focus(), DispatcherPriority.Input);
        };

        // The usual "back" gestures leave a loaded list for the link field, like the Back button.
        // They're watched on the window because focus may be anywhere after the list loads.
        Loaded += (s, e) =>
        {
            var window = Window.GetWindow(this);
            if (window == null || backGesturesHooked)
                return;

            backGesturesHooked = true;
            window.PreviewMouseUp += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.XButton1 && GoBack())
                    e.Handled = true;
            };
            window.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.System && e.SystemKey == Key.Left && GoBack())
                    e.Handled = true;
            };
        };
    }

    private bool backGesturesHooked;

    private bool GoBack()
    {
        if (!IsVisible || ViewModel?.IsLoaded != true)
            return false;

        ViewModel.ChangeLinkCommand.Execute(null);
        return true;
    }

    private DownloadViewModel ViewModel => DataContext as DownloadViewModel;

    private void LinkBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // Enter searches; Shift+Enter adds another line for a batch of links.
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            if (ViewModel?.LoadCommand.CanExecute(null) == true)
                ViewModel.LoadCommand.Execute(null);
        }
    }

    private static string DroppedText(System.Windows.IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.UnicodeText))
            return data.GetData(DataFormats.UnicodeText) as string;

        if (data.GetDataPresent(DataFormats.Text))
            return data.GetData(DataFormats.Text) as string;

        // Links dragged from a browser's address bar arrive as "UniformResourceLocatorW".
        if (data.GetDataPresent("UniformResourceLocatorW") && data.GetData("UniformResourceLocatorW") is MemoryStream stream)
            return Encoding.Unicode.GetString(stream.ToArray()).TrimEnd('\0');

        return null;
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        var hasText = DroppedText(e.Data) != null;
        e.Effects = hasText ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        SetDropHighlight(hasText);
    }

    private void DropZone_DragLeave(object sender, DragEventArgs e) => SetDropHighlight(false);

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        SetDropHighlight(false);
        var text = DroppedText(e.Data);
        if (text != null)
        {
            ViewModel?.AcceptDroppedText(text);
            e.Handled = true;
        }
    }

    private void SetDropHighlight(bool on)
    {
        DropOutline.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, on ? "Red" : "Line2");
        DropOutline.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, on ? "RedSoft" : "Transparent");
        if (!on)
            DropOutline.Fill = System.Windows.Media.Brushes.Transparent;
    }
}
