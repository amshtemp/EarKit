using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarKit.Controls;
using EarKit.Core;
using EarKit.Pages;
using EarKit.Services;
using Wpf.Ui.Controls;
using ListBoxItem = System.Windows.Controls.ListBoxItem;
using TextBlock = System.Windows.Controls.TextBlock;
namespace EarKit;

public partial class MainWindow : FluentWindow
{
    sealed record NavDef(string Key, string Title, SymbolRegular Icon, Func<bool> Visible, Func<UserControl> Make);
    readonly List<NavDef> _defs;
    readonly Dictionary<string, UserControl> _pages = new();
    readonly Dictionary<string, ListBoxItem> _items = new();
    static DeviceController C => DeviceController.I;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => App.ApplyBackdrop();
        IsVisibleChanged += (_, _) => { if (IsVisible) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(App.ApplyBackdrop)); };
        var s = AppSettings.Current;
        if (s.WinW >= MinWidth && s.WinH >= MinHeight) { Width = s.WinW; Height = s.WinH; }

        bool conn() => C.Model != null;
        _defs = new()
        {
            new("overview", "概览", SymbolRegular.Home24, () => true, () => new OverviewPage()),
            new("noise", "降噪", SymbolRegular.HeadphonesSoundWave24, () => conn() && C.Has("noise") && C.Model!.Noise.Count > 0, () => new NoisePage()),
            new("eq", "均衡器", SymbolRegular.SoundWaveCircle24, () => conn() && C.Has("eq"), () => new EqPage()),
            new("keys", "按键", SymbolRegular.Keyboard24, () => conn() && C.Has("keys"), () => new KeysPage()),
            new("multi", "多设备连接", SymbolRegular.PhoneLaptop24, () => conn() && C.Has("dual"), () => new MultiPage()),
            new("device", "耳机设置", SymbolRegular.Headphones24, () => conn() && new[] { "ear", "prompt", "autooff", "mic", "quality", "find", "reset" }.Any(C.Has), () => new DevicePage()),
            new("app", "应用设置", SymbolRegular.Settings24, () => true, () => new AppPage()),
            new("_adv", "高级", SymbolRegular.Options24, () => true, () => null!),
            new("log", "调试日志", SymbolRegular.WindowDevTools24, () => true, () => new LogPage()),
        };
        foreach (var d in _defs)
        {
            ListBoxItem it;
            if (d.Key == "_adv")
            {
                it = new ListBoxItem
                {
                    Content = new TextBlock { Text = d.Title, FontSize = 12, Margin = new Thickness(0, 10, 0, 0), Opacity = 0.6 },
                    IsHitTestVisible = false, Focusable = false,
                };
            }
            else
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var ic = Ui.Icon(d.Icon, 18); ic.Margin = new Thickness(0, 0, 14, 0); ic.VerticalAlignment = VerticalAlignment.Center;
                sp.Children.Add(ic);
                sp.Children.Add(new TextBlock { Text = d.Title, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                it = new ListBoxItem { Content = sp, Tag = d.Key };
            }
            _items[d.Key] = it;
            Nav.Items.Add(it);
        }

        C.ConnectionChanged += UpdateConn;
        C.ModelChanged += () => { UpdateNav(); UpdateConn(); };
        C.DevicesChanged += FillDevices;
        C.StateChanged += _ => UpdateFooter();
        C.Notice += (t, m, k) => AddNotice(t, m, k);

        Nav.SelectedItem = _items["overview"];
        UpdateNav(); UpdateConn(); FillDevices();
        Closing += OnClosing;
        SizeChanged += (_, _) => { if (WindowState == WindowState.Normal) { s.WinW = ActualWidth; s.WinH = ActualHeight; } };
    }

    public void Navigate(string key)
    {
        if (_items.TryGetValue(key, out var it) && it.Visibility == Visibility.Visible) Nav.SelectedItem = it;
    }

    void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem { Tag: string key }) return;
        if (!_pages.TryGetValue(key, out var page))
        {
            var d = _defs.First(x => x.Key == key);
            try { page = d.Make(); }
            catch (Exception ex) { CrashLog.Write("page " + key, ex); page = new UserControl { Content = Ui.T("页面加载失败：" + ex.Message) }; }
            _pages[key] = page;
        }
        Scroller.VerticalScrollBarVisibility = key == "log" ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        PageHost.Content = page;
        Scroller.ScrollToTop();
    }

    void UpdateNav()
    {
        foreach (var d in _defs)
        {
            bool v = d.Visible();
            _items[d.Key].Visibility = v ? Visibility.Visible : Visibility.Collapsed;
        }
        if (Nav.SelectedItem is ListBoxItem cur && cur.Visibility != Visibility.Visible) Nav.SelectedItem = _items["overview"];
    }

    void FillDevices()
    {
        DeviceBox.Items.Clear();
        foreach (var d in C.Devices) DeviceBox.Items.Add(d);
        var pref = C.PreferredDevice();
        if (pref != null) DeviceBox.SelectedItem = pref;
        if (C.Devices.Count == 0)
        {
            DeviceBox.Items.Add(AppSettings.Current.UseCom ? "未找到蓝牙 COM 口" : "未找到已配对的蓝牙设备");
            DeviceBox.SelectedIndex = 0;
        }
    }

    void UpdateConn()
    {
        var st = C.Conn;
        bool connected = C.IsConnected;
        Busy.Visibility = st == ConnState.Connecting ? Visibility.Visible : Visibility.Collapsed;
        ConnectBtn.IsEnabled = st != ConnState.Connecting;
        DeviceBox.IsEnabled = !connected && st != ConnState.Connecting;
        RefreshBtn.IsEnabled = DeviceBox.IsEnabled;
        if (connected)
        {
            ConnectBtn.Content = "断开";
            ConnectBtn.Icon = new SymbolIcon { Symbol = SymbolRegular.PlugDisconnected24 };
            ConnectBtn.Appearance = ControlAppearance.Secondary;
            ConnIcon.Symbol = SymbolRegular.BluetoothConnected24;
        }
        else
        {
            ConnectBtn.Content = st == ConnState.Connecting ? "连接中…" : "连接";
            ConnectBtn.Icon = new SymbolIcon { Symbol = SymbolRegular.PlugConnected24 };
            ConnectBtn.Appearance = ControlAppearance.Primary;
            ConnIcon.Symbol = st == ConnState.Connecting ? SymbolRegular.BluetoothSearching24 : SymbolRegular.Bluetooth24;
        }
        NavStatus.Text = connected && C.Model != null ? C.Model.Name : C.StatusText;
        NavDot.Fill = new SolidColorBrush(connected ? Color.FromRgb(0x22, 0xC5, 0x5E) : st == ConnState.Connecting ? Color.FromRgb(0xF5, 0x9E, 0x0B)
            : st == ConnState.Error ? Color.FromRgb(0xEF, 0x44, 0x44) : Colors.Gray);
        UpdateFooter();
    }

    void UpdateFooter()
    {
        string B(string k) => C.TryGet<int>(k, out var v) && v >= 0 && v <= 100 ? v + "%" : "--";
        if (!C.IsConnected || C.Model == null) { NavBattery.Text = C.IsConnected ? "" : ""; NavBattery.Visibility = Visibility.Collapsed; return; }
        NavBattery.Visibility = Visibility.Visible;
        NavBattery.Text = C.Model.IsHead ? $"电量 {B("bat_l")}" : $"左 {B("bat_l")}  右 {B("bat_r")}  盒 {B("bat_box")}";
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        RefreshBtn.IsEnabled = false;
        try { await C.RefreshDevicesAsync(); } finally { RefreshBtn.IsEnabled = !C.IsConnected; }
    }

    void Connect_Click(object sender, RoutedEventArgs e)
    {
        if (C.IsConnected) { C.Disconnect(user: true); return; }
        if (DeviceBox.SelectedItem is not DeviceEntry ent)
        {
            AddNotice("请选择设备", "请先在 Windows 蓝牙设置中配对耳机，然后点击刷新按钮。", NoticeKind.Warning);
            return;
        }
        var s = AppSettings.Current;
        C.Connect(ent, Catalog.ByKey(s.ManualModel), s.RfcommChannel);
    }

    public void AddNotice(string title, string msg, NoticeKind kind)
    {
        var bar = new InfoBar
        {
            Title = title, Message = msg, IsOpen = true, IsClosable = true, Margin = new Thickness(0, 0, 0, 6),
            Severity = kind switch { NoticeKind.Error => InfoBarSeverity.Error, NoticeKind.Warning => InfoBarSeverity.Warning, NoticeKind.Success => InfoBarSeverity.Success, _ => InfoBarSeverity.Informational },
        };
        while (Notices.Children.Count >= 3) Notices.Children.RemoveAt(0);
        Notices.Children.Add(bar);
        var ms = kind == NoticeKind.Error ? 20000 : 10000;
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) => { t.Stop(); Notices.Children.Remove(bar); };
        t.Start();
        if (!IsVisible && kind is NoticeKind.Error or NoticeKind.Warning) App.Tray?.Show(title, msg, true);
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (App.Exiting) return;
        AppSettings.Save();
        if (AppSettings.Current.CloseToTray)
        {
            e.Cancel = true;
            Hide();
            if (!AppSettings.Current.TrayHintShown)
            {
                AppSettings.Current.TrayHintShown = true; AppSettings.Save();
                App.Tray?.Show("EarKit 仍在后台运行", "单击托盘图标可重新打开窗口；右键可快速切换降噪或退出。可在“应用设置”中关闭此行为。");
            }
            return;
        }
        e.Cancel = true;
        Dispatcher.BeginInvoke(App.ExitApp);
    }
}
