using System.Windows;
using System.Windows.Threading;
using EarKit.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
namespace EarKit;

public partial class App : Application
{
    const string MutexName = "EarKit_SingleInstance_4D2A";
    const string EventName = "EarKit_ShowWindow_4D2A";
    Mutex? _mutex;
    EventWaitHandle? _showEvt;
    public static TrayService? Tray { get; private set; }
    public static HotKeyService? HotKey { get; private set; }
    public static bool Exiting { get; private set; }
    public static MainWindow MainWin => (MainWindow)Current.MainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            try { using var ev = EventWaitHandle.OpenExisting(EventName); ev.Set(); } catch { }
            Shutdown(); return;
        }
        _showEvt = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        new Thread(() =>
        {
            while (true)
            {
                try { _showEvt.WaitOne(); } catch { return; }
                if (Exiting) return;
                Dispatcher.BeginInvoke(() => ShowMain());
            }
        }) { IsBackground = true, Name = "ShowSignal" }.Start();

        DispatcherUnhandledException += (_, a) =>
        {
            CrashLog.Write("UI", a.Exception);
            a.Handled = true;
            try { System.Windows.MessageBox.Show("程序出现错误，已记录到：\n" + CrashLog.PathFile + "\n\n" + a.Exception.Message, "EarKit", System.Windows.MessageBoxButton.OK, MessageBoxImage.Warning); } catch { }
        };
        AppDomain.CurrentDomain.UnhandledException += (_, a) => CrashLog.Write("Domain", a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { CrashLog.Write("Task", a.Exception); a.SetObserved(); };

        base.OnStartup(e);
        AppSettings.Load();
        bool trayStart = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase) || a.Equals("/tray", StringComparison.OrdinalIgnoreCase));

        var ctl = new DeviceController(Dispatcher);
        var win = new MainWindow();
        MainWindow = win;
        ApplyTheme(AppSettings.Current.Theme);

        Tray = new TrayService(ShowMain, ExitApp);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(win).EnsureHandle();
        HotKey = new HotKeyService(hwnd);
        HotKey.Pressed += OnHotKey;
        ApplyHotKey(silent: true);

        if (!trayStart) win.Show();
        _ = StartAsync(ctl);
    }

    static async Task StartAsync(DeviceController ctl)
    {
        await ctl.RefreshDevicesAsync();
        ctl.StartAutoConnect();
    }

    public static bool ApplyHotKey(bool silent = false)
    {
        var s = AppSettings.Current;
        bool ok = HotKey?.Apply(s.HotkeyEnabled, s.HotkeyMods, s.HotkeyVk) ?? false;
        if (!ok && s.HotkeyEnabled && !silent)
            Tray?.Show("快捷键注册失败", $"{HotKeyService.Describe(s.HotkeyMods, s.HotkeyVk)} 可能已被其他程序占用", true);
        return ok;
    }

    static void OnHotKey()
    {
        var c = DeviceController.I;
        var lbl = c.CycleNoise();
        if (lbl != null) Tray?.Show("降噪模式", lbl);
        else if (!c.IsConnected) Tray?.Show("EarKit", "耳机未连接");
    }

    static bool _hooked;
    static DispatcherTimer? _themeTimer;

    public static bool IsDark(string theme) => theme switch { "dark" => true, "light" => false, _ => Backdrop.SystemIsDark() };

    public static void ApplyTheme(string theme)
    {
        var win = Current.MainWindow;
        try
        {
            bool dark = IsDark(theme);
            // 只切换资源字典，背景效果交给 Backdrop 统一处理（不使用 SystemThemeWatcher，避免竞争导致主题错乱）
            ApplicationThemeManager.Apply(dark ? ApplicationTheme.Dark : ApplicationTheme.Light, WindowBackdropType.None, true);
            if (win == null) return;
            HookSystemTheme(win);
            ApplyBackdrop();
            // WPF-UI 可能在之后异步刷新窗口背景，空闲时再应用一次确保最终状态一致
            win.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(ApplyBackdrop));
        }
        catch (Exception e) { CrashLog.Write("theme", e); }
    }

    public static void ApplyBackdrop()
    {
        var win = Current.MainWindow;
        if (win == null) return;
        var s = AppSettings.Current;
        try { Backdrop.Apply(win, IsDark(s.Theme), s.Backdrop, s.BackdropOpacity); }
        catch (Exception e) { CrashLog.Write("backdrop", e); }
    }

    static void HookSystemTheme(Window win)
    {
        if (_hooked) return;
        _hooked = true;
        var h = new System.Windows.Interop.WindowInteropHelper(win).EnsureHandle();
        var src = System.Windows.Interop.HwndSource.FromHwnd(h);
        _themeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _themeTimer.Tick += (_, _) =>
        {
            _themeTimer!.Stop();
            if (AppSettings.Current.Theme == "system") ApplyTheme("system");
            else ApplyBackdrop();
        };
        src?.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
        {
            const int WM_SETTINGCHANGE = 0x001A, WM_DWMCOMPOSITIONCHANGED = 0x031E, WM_THEMECHANGED = 0x031A;
            if (msg == WM_SETTINGCHANGE || msg == WM_DWMCOMPOSITIONCHANGED || msg == WM_THEMECHANGED)
            { _themeTimer.Stop(); _themeTimer.Start(); }
            return IntPtr.Zero;
        });
    }

    public static void ShowMain()
    {
        var w = Current.MainWindow;
        if (w == null) return;
        if (!w.IsVisible) w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
        w.Topmost = true; w.Topmost = false;
        w.Focus();
    }

    public static void ExitApp()
    {
        Exiting = true;
        try { DeviceController.I?.Disconnect(); } catch { }
        try { AppSettings.SaveNow(); } catch { }
        try { HotKey?.Dispose(); } catch { }
        try { Tray?.Dispose(); } catch { }
        try { ((App)Current)._showEvt?.Set(); } catch { }
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Tray?.Dispose(); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }
}
