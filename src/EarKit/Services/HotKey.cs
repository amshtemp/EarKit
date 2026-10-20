using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
namespace EarKit.Services;

public sealed class HotKeyService : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    public const int MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
    const int WM_HOTKEY = 0x0312, ID = 0xEF01;
    readonly IntPtr _hwnd;
    bool _reg;
    public event Action? Pressed;

    public HotKeyService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        HwndSource.FromHwnd(hwnd)?.AddHook(Hook);
    }
    IntPtr Hook(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_HOTKEY && w.ToInt32() == ID) { handled = true; Pressed?.Invoke(); }
        return IntPtr.Zero;
    }
    public bool Apply(bool enabled, int mods, int vk)
    {
        if (_reg) { UnregisterHotKey(_hwnd, ID); _reg = false; }
        if (!enabled || vk == 0) return true;
        _reg = RegisterHotKey(_hwnd, ID, (uint)(mods | MOD_NOREPEAT), (uint)vk);
        return _reg;
    }
    public static string Describe(int mods, int vk)
    {
        if (vk == 0) return "未设置";
        var p = new List<string>();
        if ((mods & MOD_CONTROL) != 0) p.Add("Ctrl");
        if ((mods & MOD_ALT) != 0) p.Add("Alt");
        if ((mods & MOD_SHIFT) != 0) p.Add("Shift");
        if ((mods & MOD_WIN) != 0) p.Add("Win");
        var k = KeyInterop.KeyFromVirtualKey(vk);
        var ks = k switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(k - Key.D0)).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => "Num" + (int)(k - Key.NumPad0),
            _ => k.ToString(),
        };
        p.Add(ks);
        return string.Join(" + ", p);
    }
    public void Dispose() { if (_reg) UnregisterHotKey(_hwnd, ID); _reg = false; }
}
