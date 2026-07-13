using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NovaSetlist.Ui;

/// <summary>Turns on the dark window title bar (same trick as TimecodeBridge).</summary>
internal static class Dwm
{
    private const int UseImmersiveDarkMode = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void UseDarkTitleBar(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).EnsureHandle();
            var dark = 1;
            _ = DwmSetWindowAttribute(handle, UseImmersiveDarkMode, ref dark, sizeof(int));
        }
        catch
        {
            // Cosmetic only — older Windows builds just keep the light title bar.
        }
    }
}
