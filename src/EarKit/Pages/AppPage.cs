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
        Root.Children.Add(Ui.Header("Application Settings", "EarKit's own appearance, startup, tray, notifications and connection options."));

        // Appearance
        Root.Children.Add(Ui.Section("Appearance"));
        _theme = Ui.Combo(new[] { "Follow the system", "Light", "Dark" }, 160);
        _theme.SelectedIndex = S.Theme switch { "light" => 1, "dark" => 2, _ => 0 };
        _theme.SelectionChanged += (_, _) =>
        {
            if (Sync) return;
            S.Theme = _theme.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "system" };
            AppSettings.Save(); App.ApplyTheme(S.Theme);
        };
        Root.Children.Add(Ui.Row(W.SymbolRegular.WeatherMoon24, "Theme", "Choose Light, Dark or Follow Windows", _theme));
        var fx = new[] { "mica", "acrylic", "tabbed", "none" };
        var fxCombo = Ui.Combo(new[] { "Mica", "Acrylic", "Mica·Tabbed", "Off(Solid color background)" }, 200);
        fxCombo.SelectedIndex = Math.Max(0, Array.IndexOf(fx, S.Backdrop));
        var opSlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Width = 200,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            Value = S.BackdropOpacity,
            VerticalAlignment = VerticalAlignment.Center
        };
        var opText = Ui.Fg(Ui.Plain($"{S.BackdropOpacity}%", 13), "TextFillColorSecondaryBrush");
        opText.Width = 44; opText.TextAlignment = TextAlignment.Right; opText.VerticalAlignment = VerticalAlignment.Center;
        var opPanel = new StackPanel { Orientation = Orientation.Horizontal };
        opPanel.Children.Add(opSlider); opPanel.Children.Add(opText);
        var rOp = Ui.Row(W.SymbolRegular.Blur24, "Background opacity", "The larger the value, the more solid the background and the clearer the text; the smaller the value, the more obvious the transparency effect", opPanel);
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
        Root.Children.Add(Ui.Row(W.SymbolRegular.Color24, "Background Effect",
            supported ? "Transparent material for window background" : "Requires Windows 11 22H2 or later, currently using solid color background", fxCombo));
        Root.Children.Add(rOp);

        //Start and window
        Root.Children.Add(Ui.Section("Startup and Window"));
        _auto = Ui.Toggle(v => { if (Sync) return; AutoStart.Set(v, S.StartMinimized); Ui.Show(_rMin!, v); });
        _auto.IsChecked = AutoStart.IsEnabled();
        Root.Children.Add(Ui.Row(W.SymbolRegular.Power24, "Start automatically at boot", "Automatically run EarKit after logging in to Windows", _auto));
        _min = Ui.Toggle(v => { if (Sync) return; S.StartMinimized = v; AppSettings.Save(); if (AutoStart.IsEnabled()) AutoStart.Set(true, v); });
        _min.IsChecked = S.StartMinimized;
        _rMin = Ui.Row(W.SymbolRegular.ArrowExit24, "Minimize to the tray when starting up", "No pop-up window when starting up, only display the icon in the tray", _min);
        Ui.Show(_rMin, AutoStart.IsEnabled());
        Root.Children.Add(_rMin);
        _tray = Ui.Toggle(v => { if (Sync) return; S.CloseToTray = v; AppSettings.Save(); });
        _tray.IsChecked = S.CloseToTray;
        Root.Children.Add(Ui.Row(W.SymbolRegular.Apps24, "Minimize to the tray when closing the window", "The program continues to run in the background after closing and can be opened or exited from the tray icon", _tray));

        // connect
        Root.Children.Add(Ui.Section("Connection"));
        _autoConn = Ui.Toggle(v => { if (Sync) return; S.AutoConnect = v; AppSettings.Save(); });
        _autoConn.IsChecked = S.AutoConnect;
        Root.Children.Add(Ui.Row(W.SymbolRegular.BluetoothConnected24, "Automatically connect to the last headset", "Automatically connect when starting up and when the headset is reconnected to the computer", _autoConn));
        _model = Ui.Combo(new[] { "Automatic recognition (recommended)" }.Concat(Catalog.MODELS.Select(m => $"{m.Name} [{m.Key}]")), 280);
        var mi = Catalog.MODELS.FindIndex(m => m.Key == S.ManualModel);
        _model.SelectedIndex = mi + 1;
        _model.SelectionChanged += (_, _) =>
        {
            if (Sync) return;
            var i = _model.SelectedIndex;
            C.SetManualModel(i > 0 ? Catalog.MODELS[i - 1] : null);
        };
        Root.Children.Add(Ui.Row(W.SymbolRegular.Headphones24, "Headphone model", "Usually no modification is required. If the automatic recognition error occurs, you can manually specify it here", _model));

        _connMode = Ui.Combo(new[] { "Bluetooth direct connection (recommended)", "Bluetooth serial port COM port" }, 200);
        _connMode.SelectedIndex = S.UseCom ? 1 : 0;
        _com = new ComboBox { MinWidth = 120, Margin = new Thickness(8, 0, 0, 0) };
        _chan = new TextBox { Width = 64, Text = S.RfcommChannel.ToString(), Margin = new Thickness(8, 0, 0, 0), ToolTip = "RFCOMM channel, 0 = automatic" };
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
        _rCom = Ui.Row(W.SymbolRegular.PlugConnected24, "Connection method (advanced)",
            "By default, connect directly over Bluetooth RFCOMM. If that fails, add an outgoing port for the headset under Windows Settings > Bluetooth & devices > More Bluetooth settings > COM Ports, then switch to COM. The number on the right is the RFCOMM channel (0 = automatic).", adv);
        Root.Children.Add(_rCom);

        // Notifications
        Root.Children.Add(Ui.Section("Notifications"));
        _notify = Ui.Toggle(v => { if (Sync) return; S.NotifyConnect = v; AppSettings.Save(); });
        _notify.IsChecked = S.NotifyConnect;
        Root.Children.Add(Ui.Row(W.SymbolRegular.Alert24, "Connection notifications", "Show a tray notification when the headset connects or disconnects", _notify));
        _lowBat = Ui.Toggle(v => { if (Sync) return; S.LowBatteryAlert = v; AppSettings.Save(); });
        _lowBat.IsChecked = S.LowBatteryAlert;
        _bat = Ui.Combo(new[] { "10%", "15%", "20%", "30%" }, 90);
        _bat.SelectedItem = $"{S.LowBatteryThreshold}%";
        if (_bat.SelectedIndex < 0) _bat.SelectedIndex = 2;
        _bat.SelectionChanged += (_, _) => { if (Sync) return; S.LowBatteryThreshold = int.Parse(((string)_bat.SelectedItem).TrimEnd('%')); AppSettings.Save(); };
        var bp = new StackPanel { Orientation = Orientation.Horizontal };
        bp.Children.Add(_bat); _bat.Margin = new Thickness(0, 0, 12, 0);
        bp.Children.Add(_lowBat);
        Root.Children.Add(Ui.Row(W.SymbolRegular.BatteryWarning24, "Low battery alert", "Alert when either earbud falls below the threshold (not while charging)", bp));

        // Hotkey
        Root.Children.Add(Ui.Section("Hotkey"));
        _hkBox = new TextBox { Width = 180, IsReadOnly = true, Text = HotKeyService.Describe(S.HotkeyMods, S.HotkeyVk), ToolTip = "Click here, then press a new key combination" };
        _hkBox.PreviewKeyDown += HkKeyDown;
        _hk = Ui.Toggle(v => { if (Sync) return; S.HotkeyEnabled = v; AppSettings.Save(); App.ApplyHotKey(); });
        _hk.IsChecked = S.HotkeyEnabled;
        var hp = new StackPanel { Orientation = Orientation.Horizontal };
        hp.Children.Add(_hkBox); _hkBox.Margin = new Thickness(0, 0, 12, 0);
        hp.Children.Add(_hk);
        Root.Children.Add(Ui.Row(W.SymbolRegular.Keyboard24, "Global hotkey: cycle noise control", "Press this shortcut in any app to cycle through Noise Cancellation / Ambient / Off. Click the box and press a new combination (must include Ctrl / Alt / Shift / Win).", hp));

        // Experimental
        Root.Children.Add(Ui.Section("Experimental"));
        _unlock = Ui.Toggle(v => { if (Sync) return; C.SetUnlock(v); });
        _unlock.IsChecked = S.UnlockAll;
        Root.Children.Add(Ui.Row(W.SymbolRegular.Shield24, "Unlock all features", "Show features not declared as supported by this model. They may not work or may behave unexpectedly; use with caution.", _unlock));

        // About
        Root.Children.Add(Ui.Section("About"));
        var ver = typeof(App).Assembly.GetName().Version;
        var open = Ui.Btn("Open data folder", W.SymbolRegular.DocumentText24, () =>
        {
            try { Directory.CreateDirectory(AppSettings.Dir); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.Dir}\"") { UseShellExecute = true }); } catch { }
        });
        var lic = Ui.Btn("Open-source licenses", W.SymbolRegular.Document24, () =>
        {
            var f = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.txt");
            var l = Path.Combine(AppContext.BaseDirectory, "LICENSE.txt");
            try { foreach (var x in new[] { l, f }) if (File.Exists(x)) Process.Start(new ProcessStartInfo(x) { UseShellExecute = true }); } catch { }
        });
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        btns.Children.Add(lic); lic.Margin = new Thickness(0, 0, 8, 0); btns.Children.Add(open);
        Root.Children.Add(Ui.Row(W.SymbolRegular.Info24, $"EarKit {ver?.ToString(3)}",
            "An open-source third-party headset control app compatible with 23 EarFun models (Qualcomm GAIA / EF protocols). EarFun is a trademark of its owner. This software is not affiliated with or endorsed by EarFun. It is provided “as is” without warranty. Settings and crash logs are stored in %AppData%\\EarKit.", btns));
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
        if (mods == 0) { _hkBox.Text = "Must include Ctrl / Alt / Shift"; return; }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        S.HotkeyMods = mods; S.HotkeyVk = vk; AppSettings.Save();
        _hkBox.Text = HotKeyService.Describe(mods, vk);
        if (S.HotkeyEnabled && !App.ApplyHotKey()) _hkBox.Text += " (in use)";
    }

    protected override void OnState(StateUpdate s) { }
    protected override void OnAll()
    {
        var mi = Catalog.MODELS.FindIndex(m => m.Key == S.ManualModel);
        _model.SelectedIndex = mi + 1;
        _unlock.IsChecked = S.UnlockAll;
    }
}