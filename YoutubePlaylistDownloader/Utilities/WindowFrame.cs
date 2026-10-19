using System.Windows.Interop;

namespace YoutubePlaylistDownloader.Utilities;

public static class WindowFrame
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    /// <summary>Rounded corners, a border that matches the theme and dark title bar/system menus on Windows 11 (no-op elsewhere).</summary>
    public static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        try
        {
            var dark = ThemeService.IsDark ? 1 : 0;
            DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));

            var corner = DWMWCP_ROUND;
            DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

            // COLORREF is 0x00BBGGRR.
            var border = ThemeService.IsDark ? 0x00303030 : 0x00D9D9D9;
            DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch
        {
            // Older Windows versions don't know these attributes.
        }
    }
}
