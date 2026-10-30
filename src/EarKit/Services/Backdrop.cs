using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
namespace EarKit.Services;

/// <summary>Controls the window background effect (Mica, Acrylic, or solid) through DWM to avoid conflicts with WPF-UI.</summary>
public static class Backdrop
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS m);
    [StructLayout(LayoutKind.Sequential)] struct MARGINS { public int L, R, T, B; }

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_SYSTEMBACKDROP_TYPE = 38;

    /// <summary>System backdrop effects require Windows 11 22H2 (22621) or later.</summary>
    public static bool Supported => Environment.OSVersion.Version.Build >= 22621;

    public static bool SystemIsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    public static void Apply(Window w, bool dark, string effect, int opacity)
    {
        var h = new WindowInteropHelper(w).EnsureHandle();
        var src = HwndSource.FromHwnd(h);
        int d = dark ? 1 : 0;
        DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref d, 4);

        var baseC = dark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);
        int type = effect switch { "mica" => 2, "acrylic" => 3, "tabbed" => 4, _ => 1 };
        if (!Supported) type = 1;

        if (Supported) DwmSetWindowAttribute(h, DWMWA_SYSTEMBACKDROP_TYPE, ref type, 4);
        if (type == 1)
        {
            if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = baseC;
            w.Background = new SolidColorBrush(baseC);
        }
        else
        {
            var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(w);
            if (chrome != null && chrome.GlassFrameThickness != new Thickness(-1)) chrome.GlassFrameThickness = new Thickness(-1);
            var m = new MARGINS { L = -1, R = -1, T = -1, B = -1 };
            DwmExtendFrameIntoClientArea(h, ref m);
            if (src?.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
            byte a = (byte)Math.Round(255 * Math.Clamp(opacity, 0, 100) / 100.0);
            w.Background = new SolidColorBrush(Color.FromArgb(a, baseC.R, baseC.G, baseC.B));
        }
    }
}
