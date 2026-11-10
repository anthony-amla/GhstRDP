using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace GhstRDP.Core;

/// <summary>Ajustes de janela: barra escura do Windows 11, cantos arredondados e margem ao maximizar.</summary>
public static class NativeWindow
{
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int index);

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    const int DWMWA_BORDER_COLOR = 34;
    const int DWMWA_CAPTION_COLOR = 35;

    const int SM_CXFRAME = 32, SM_CYFRAME = 33, SM_CXPADDEDBORDER = 92;

    public static void ApplyDarkChrome(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int on = 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
            int round = 2; // cantos arredondados do Windows 11
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int caption = 0x00100D0D; // #0D0D10 em COLORREF (BGR)
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
            int border = 0x00363030;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch
        {
            // Windows 10 antigo: ignora
        }
    }

    /// <summary>Quanto a janela maximizada "vaza" da tela (em DIPs) quando usamos WindowChrome.</summary>
    public static Thickness MaximizedOverhang(Visual visual)
    {
        var dpi = VisualTreeHelper.GetDpi(visual);
        double fx = GetSystemMetrics(SM_CXFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
        double fy = GetSystemMetrics(SM_CYFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
        return new Thickness(fx / dpi.DpiScaleX, fy / dpi.DpiScaleY, fx / dpi.DpiScaleX, fy / dpi.DpiScaleY);
    }
}
