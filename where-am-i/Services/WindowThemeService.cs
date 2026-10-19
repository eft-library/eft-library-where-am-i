using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace where_am_i.Services;

internal static class WindowThemeService
{
    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            var dark = 1;
            // Older Windows versions may reject newer attributes; the window remains usable.
            if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) < 0)
                DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
            var caption = 0x00181411; // COLORREF for #111418.
            DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
            DwmSetWindowAttribute(handle, 34, ref caption, sizeof(int));
        };
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
