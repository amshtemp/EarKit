using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EarKit.Controls;
using EarKit.Core;
using EarKit.Services;
using W = Wpf.Ui.Controls;
namespace EarKit.Pages;

public sealed class AppPage : PageBase
{
    readonly ComboBox _theme, _model, _bat, _connMode, _com;
    readonly W.ToggleSwitch _auto, _min, _tray, _autoConn, _lowBat, _hk, _unlock, _notify;
    readonly TextBox _hkBox, _chan;
    readonly Border _rMin, _rCom;

    public AppPage()
    {
        Sync = true;
        Root.Children.Add(Ui.Header("应用设置", "EarKit 自身的外观、启动、托盘、通知与连接选项。"));

        // 外观
        Root.Children.Add(Ui.Section("外观"));
        _theme = Ui.Combo(new[] { "跟随系统", "浅色", "深色" }, 160);
        _theme.SelectedIndex = S.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        _theme.SelectionChanged += (_, _) =>
        {
            if (Sync) return;
            S.Theme = _theme.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
            AppSettings.Save(); App.ApplyTheme(S.Theme);
        };
        Root.Children.Add(Ui.Row(W.SymbolRegular.WeatherMoon24, "主题", "选择浅色、深色或跟随 Windows", _theme));

        var fx = new[] { "mica", "acrylic", "tabbed", "none" };
        var fxCombo = Ui.Combo(new[] { "云母（Mica）", "亚克力（Acrylic）", "云母 · 加深（Tabbed）", "关闭（纯色背景）" }, 200);
        fxCombo.SelectedIndex = Math.Max(0, Array.IndexOf(fx, S.Backdrop));
        var opSlider = new Slider { Minimum = 0, Maximum = 100, Width = 200, TickFrequency = 5, IsSnapToTickEnabled = true,
            Value = S.BackdropOpacity, VerticalAlignment = VerticalAlignment.Center };
        var opText = Ui.Fg(Ui.Plain($"{S.BackdropOpacity}%", 13), "TextFillColorSecondaryBrush");
        opText.Width = 44; opText.TextAlignment = TextAlignment.Right; opText.VerticalAlignment = VerticalAlignment.Center;
        var opPanel = new StackPanel { Orientation = Orientation.Horizontal };
        opPanel.Children.Add(opSlider); opPanel.Children.Add(opText);
        var rOp = Ui.Row(W.SymbolRegular.Blur24, "背景不透明度", "数值越大背景越实、文字越清晰；数值越小透明效果越明显", opPanel);
        bool supported = Backdrop.Supported;
        fxCombo.IsEnabled = supported;
        Ui.Show(rOp, supported && S.Backdrop != "none");
        fxCombo.SelectionChanged += (_, _) =>
        {
            if (Sync) return;
            S.Backdrop = fx[Math.Max(0, fxCombo.SelectedIndex)];
            AppSettings.Save(); App.ApplyBackdrop();
            Ui.Show(rOp, S.Backdrop != "none");
        };
        opSlider.ValueChanged += (_, _) =>
        {
            if (Sync) return;
            S.BackdropOpacity = (int)Math.Round(opSlider.Value);
            opText.Text = $"{S.BackdropOpacity}%";
            AppSettings.Save(); App.ApplyBackdrop();
        };
        Root.Children.Add(Ui.Row(W.SymbolRegular.Color24, "背景效果",
            supported ? "窗口背景的透明材质" : "需要 Windows 11 22H2 或更高版本，当前使用纯色背景", fxCombo));
        Root.Children.Add(rOp);

        // 启动与窗口
        Root.Children.Add(Ui.Section("启动与窗口"));
        _auto = Ui.Toggle(v => { if (Sync) return; AutoStart.Set(v, S.StartMinimized); Ui.Show(_rMin!, v); });
        _auto.IsChecked = AutoStart.IsEnabled();
        Root.Children.Add(Ui.Row(W.SymbolRegular.Power24, "开机自动启动", "登录 Windows 后自动运行 EarKit", _auto));
        _min = Ui.Toggle(v => { if (Sync) return; S.StartMinimized = v; AppSettings.Save(); if (AutoStart.IsEnabled()) AutoStart.Set(true, v); });
        _min.IsChecked = S.StartMinimized;
        _rMin = Ui.Row(W.SymbolRegular.ArrowExit24, "自启动时最小化到托盘", "开机启动时不弹出窗口，只在托盘显示图标", _min);
        Ui.Show(_rMin, AutoStart.IsEnabled());
        Root.Children.Add(_rMin);
        _tray = Ui.Toggle(v => { if (Sync) return; S.CloseToTray = v; AppSettings.Save(); });
        _tray.IsChecked = S.CloseToTray;
        Root.Children.Add(Ui.Row(W.SymbolRegular.Apps24, "关闭窗口时最小化到托盘", "关闭后程序继续在后台运行，可从托盘图标打开或退出", _tray));

        // 连接
        Root.Children.Add(Ui.Section("连接"));
        _autoConn = Ui.Toggle(v => { if (Sync) return; S.AutoConnect = v; AppSettings.Save(); });
        _autoConn.IsChecked = S.AutoConnect;
        Root.Children.Add(Ui.Row(W.SymbolRegular.BluetoothConnected24, "自动连接上次的耳机", "启动时以及耳机重新连上电脑后自动连接", _autoConn));
        _model = Ui.Combo(new[] { "自动识别（推荐）" }.Concat(Catalog.MODELS.Select(m => $"{m.Name}  [{m.Key}]")), 280);
        var mi = Catalog.MODELS.FindIndex(m => m.Key == S.ManualModel);
        _model.SelectedIndex = mi + 1;
        _model.SelectionChanged += (_, _) =>
        {
            if (Sync) return;
            var i = _model.SelectedIndex;
            C.SetManualModel(i > 0 ? Catalog.MODELS[i - 1] : null);
        };
        Root.Children.Add(Ui.Row(W.SymbolRegular.Headphones24, "耳机型号", "通常无需修改。若自动识别错误，可在此手动指定", _model));

        _connMode = Ui.Combo(new[] { "蓝牙直连（推荐）", "蓝牙串口 COM 口" }, 200);
        _connMode.SelectedIndex = S.UseCom ? 1 : 0;
        _com = new ComboBox { MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        _chan = new TextBox { Width = 64, Text = S.RfcommChannel.ToString(), Margin = new Thickness(8, 0, 0, 0), ToolTip = "RFCOMM 通道，0 = 自动" };
        var adv = new StackPanel { Orientation = Orientation.Horizontal };
        adv.Children.Add(_connMode);
        adv.Children.Add(_chan);
        _connMode.SelectionChanged += async (_, _) =>
        {
            if (Sync) return;
            S.UseCom = _connMode.SelectedIndex == 1; AppSettings.Save();
            Ui.Show(_chan, !S.UseCom);
            await C.RefreshDevicesAsync();
        };
        Ui.Show(_chan, !S.UseCom);
        _chan.LostFocus += (_, _) =>
        {
            if (int.TryParse(_chan.Text.Trim(), out var c) && c >= 0 && c <= 30) { S.RfcommChannel = c; AppSettings.Save(); }
            else _chan.Text = S.RfcommChannel.ToString();
        };
        _rCom = Ui.Row(W.SymbolRegular.PlugConnected24, "连接方式（高级）",
            "默认通过蓝牙 RFCOMM 直接连接。若连接失败，可在 Windows“更多蓝牙设置 → COM 端口”中为耳机添加“传出”端口后改用 COM 口；右侧数字为 RFCOMM 通道（0 = 自动）。", adv);
        Root.Children.Add(_rCom);

        // 通知
        Root.Children.Add(Ui.Section("通知"));
        _notify = Ui.Toggle(v => { if (Sync) return; S.NotifyConnect = v; AppSettings.Save(); });
        _notify.IsChecked = S.NotifyConnect;
        Root.Children.Add(Ui.Row(W.SymbolRegular.Alert24, "连接状态通知", "耳机连接或断开时在托盘弹出通知", _notify));
        _lowBat = Ui.Toggle(v => { if (Sync) return; S.LowBatteryAlert = v; AppSettings.Save(); });
        _lowBat.IsChecked = S.LowBatteryAlert;
        _bat = Ui.Combo(new[] { "10%", "15%", "20%", "30%" }, 90);
        _bat.SelectedItem = $"{S.LowBatteryThreshold}%";
        if (_bat.SelectedIndex < 0) _bat.SelectedIndex = 2;
        _bat.SelectionChanged += (_, _) => { if (Sync) return; S.LowBatteryThreshold = int.Parse(((string)_bat.SelectedItem).TrimEnd('%')); AppSettings.Save(); };
        var bp = new StackPanel { Orientation = Orientation.Horizontal };
        bp.Children.Add(_bat); _bat.Margin = new Thickness(0, 0, 12, 0);
        bp.Children.Add(_lowBat);
        Root.Children.Add(Ui.Row(W.SymbolRegular.BatteryWarning24, "低电量提醒", "左耳或右耳电量低于阈值时提醒（充电中不提醒）", bp));

        // 快捷键
        Root.Children.Add(Ui.Section("快捷键"));
        _hkBox = new TextBox { Width = 180, IsReadOnly = true, Text = HotKeyService.Describe(S.HotkeyMods, S.HotkeyVk), ToolTip = "点击后按下新的组合键" };
        _hkBox.PreviewKeyDown += HkKeyDown;
        _hk = Ui.Toggle(v => { if (Sync) return; S.HotkeyEnabled = v; AppSettings.Save(); App.ApplyHotKey(); });
        _hk.IsChecked = S.HotkeyEnabled;
        var hp = new StackPanel { Orientation = Orientation.Horizontal };
        hp.Children.Add(_hkBox); _hkBox.Margin = new Thickness(0, 0, 12, 0);
        hp.Children.Add(_hk);
        Root.Children.Add(Ui.Row(W.SymbolRegular.Keyboard24, "全局快捷键：切换降噪模式", "在任何程序中按下即可循环切换降噪 / 通透 / 关闭。点击输入框后按下新的组合键（需包含 Ctrl / Alt / Shift / Win）", hp));

        // 实验
        Root.Children.Add(Ui.Section("实验功能"));
        _unlock = Ui.Toggle(v => { if (Sync) return; C.SetUnlock(v); });
        _unlock.IsChecked = S.UnlockAll;
        Root.Children.Add(Ui.Row(W.SymbolRegular.Shield24, "解锁全部功能", "显示当前型号未声明支持的功能。可能无效或产生意外行为，请谨慎使用", _unlock));

        // 关于
        Root.Children.Add(Ui.Section("关于"));
        var ver = typeof(App).Assembly.GetName().Version;
        var open = Ui.Btn("打开数据文件夹", W.SymbolRegular.DocumentText24, () =>
        {
            try { Directory.CreateDirectory(AppSettings.Dir); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.Dir}\"") { UseShellExecute = true }); } catch { }
        });
        var lic = Ui.Btn("开源许可", W.SymbolRegular.Document24, () =>
        {
            var f = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
            var l = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
            try { foreach (var x in new[] { l, f }) if (File.Exists(x)) Process.Start(new ProcessStartInfo(x) { UseShellExecute = true }); } catch { }
        });
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        btns.Children.Add(lic); lic.Margin = new Thickness(0, 0, 8, 0); btns.Children.Add(open);
        Root.Children.Add(Ui.Row(W.SymbolRegular.Info24, $"EarKit {ver?.ToString(3)}",
            "开源的第三方耳机控制工具，兼容 23 款 EarFun 耳机（高通 GAIA / EF 协议）。EarFun 是其所有者的商标，本软件与 EarFun 公司无关，也未获其认可。本软件按“原样”提供，不附带任何担保。设置与崩溃日志保存在 %AppData%\\EarKit。", btns));
        Sync = false;
    }

    void HkKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        int mods = 0;
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods |= HotKeyService.MOD_CONTROL;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= HotKeyService.MOD_ALT;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= HotKeyService.MOD_SHIFT;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= HotKeyService.MOD_WIN;
        if (mods == 0) { _hkBox.Text = "需包含 Ctrl / Alt / Shift"; return; }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        S.HotkeyMods = mods; S.HotkeyVk = vk; AppSettings.Save();
        _hkBox.Text = HotKeyService.Describe(mods, vk);
        if (S.HotkeyEnabled && !App.ApplyHotKey()) _hkBox.Text += "（被占用）";
    }

    protected override void OnState(StateUpdate s) { }
    protected override void OnAll()
    {
        var mi = Catalog.MODELS.FindIndex(m => m.Key == S.ManualModel);
        _model.SelectedIndex = mi + 1;
        _unlock.IsChecked = S.UnlockAll;
    }
}
