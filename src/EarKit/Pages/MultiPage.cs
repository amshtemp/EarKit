using System.Windows;
using System.Windows.Controls;
using EarKit.Controls;
using EarKit.Core;
using EarKit.Services;
using W = Wpf.Ui.Controls;
namespace EarKit.Pages;

public sealed class MultiPage : PageBase
{
    readonly W.ToggleSwitch _dual;
    readonly W.InfoBar _tips = new() { IsOpen = true, IsClosable = false, Severity = W.InfoBarSeverity.Informational, Title = "Information", Margin = new Thickness(0, 8, 0, 0) };
    readonly StackPanel _body = new();
    readonly TextBlock _count;
    readonly StackPanel _on = new(), _off = new(), _offBox = new();
    readonly W.Button _newBtn;
    readonly Border _pairPanel;
    readonly TextBlock _pairText;

    public MultiPage()
    {
        Root.Children.Add(Ui.Header("Multipoint", "Connect the headset to two devices at once (such as a phone and computer), then view and switch connected devices here."));
        _dual = Ui.Toggle(User<bool>(OnToggle));
        Root.Children.Add(Ui.Row(W.SymbolRegular.PhoneLaptop24, "Multipoint", "Connect two devices at once and switch automatically for calls or playback", _dual));
        Root.Children.Add(_tips);

        var hdr = new Grid { Margin = new Thickness(0, 18, 0, 8) };
        hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var t = Ui.T("Connected devices", "SectionTitle"); t.Margin = new Thickness(2, 0, 10, 0); t.VerticalAlignment = VerticalAlignment.Center;
        titleRow.Children.Add(t);
        _count = new TextBlock { Text = "0/2", FontSize = 12, FontWeight = FontWeights.SemiBold };
        var badge = new Border { Child = _count, VerticalAlignment = VerticalAlignment.Center };
        badge.SetResourceReference(StyleProperty, "Tag");
        titleRow.Children.Add(badge);
        hdr.Children.Add(titleRow);
        var refresh = Ui.Btn("Refresh list", W.SymbolRegular.ArrowSync24, () => C.RefreshDevList());
        Grid.SetColumn(refresh, 1); hdr.Children.Add(refresh);
        _body.Children.Add(hdr);
        _body.Children.Add(_on);

        _offBox.Children.Add(Ui.Section("Paired but disconnected"));
        _offBox.Children.Add(_off);
        _body.Children.Add(_offBox);

        _body.Children.Add(Ui.Section("Connect a new device"));
        _newBtn = Ui.Btn("Put headset in pairing mode…", W.SymbolRegular.Add24, () => C.StartPairing(), W.ControlAppearance.Primary);
        var stop = Ui.Btn("Stop pairing", W.SymbolRegular.Dismiss24, () => C.StopPairing(false));
        var ring = new W.ProgressRing { IsIndeterminate = true, Width = 28, Height = 28, Margin = new Thickness(0, 0, 16, 0) };
        _pairText = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var pg = new Grid();
        pg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pg.Children.Add(ring);
        Grid.SetColumn(_pairText, 1); pg.Children.Add(_pairText);
        stop.Margin = new Thickness(16, 0, 0, 0); stop.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(stop, 2); pg.Children.Add(stop);
        _pairPanel = Ui.Panel(pg);
        _body.Children.Add(_newBtn);
        _body.Children.Add(_pairPanel);
        Root.Children.Add(_body);

        C.PhonesChanged += () => Guard(OnAll);
        C.PairingChanged += () => Guard(OnAll);
        Guard(OnAll);
    }

    async void OnToggle(bool on)
    {
        bool offLdac = false;
        if (C.LdacConflict(on))
        {
            if (!await Ui.Confirm("LDAC conflicts with multipoint", "LDAC and multipoint cannot be used at the same time (the official app also blocks this).\n\nTurn off LDAC and enable multipoint?", "Turn off LDAC and enable"))
            {
                Guard(() => _dual.IsChecked = false);
                return;
            }
            offLdac = true;
        }
        var cfg = C.Dual;
        if ((on && cfg.OpenReset) || cfg.ToggleReset)
        {
            if (!await Ui.Confirm("Headset will restart", $"This model restarts automatically to apply the multipoint setting. The connection will drop, and EarKit will reconnect in about 20 seconds.\n\nContinue?", "Continue"))
            {
                Guard(() => _dual.IsChecked = !on);
                return;
            }
        }
        C.SetDual(on, offLdac);
    }

    Border Card(PhoneInfo p, bool connected)
    {
        bool pc = DeviceController.IsThisPc(p);
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var ic = Ui.Icon(pc ? W.SymbolRegular.Laptop24 : W.SymbolRegular.Phone24, 26);
        ic.Margin = new Thickness(2, 0, 16, 0); ic.VerticalAlignment = VerticalAlignment.Center;
        if (connected) Ui.Fg(ic, "AccentTextFillColorPrimaryBrush");
        g.Children.Add(ic);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(Ui.T(string.IsNullOrEmpty(p.Name) ? "Unknown device" : p.Name, "RowTitle"));
        var st = connected ? "Connected" : "Disconnected";
        if (connected && C.Plays.TryGetValue(p.Mac, out var pl)) st += pl == 1 ? " · Playing" : pl == 2 ? " · Paused" : "";
        if (pc) st += " · This PC?";
        info.Children.Add(Ui.T($"{st}   ·   Address …{p.Mac}", "RowDesc"));
        Grid.SetColumn(info, 1); g.Children.Add(info);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (connected)
        {
            if (C.Dual.Play)
            {
                btns.Children.Add(Ui.Btn("Set active", W.SymbolRegular.Play24, () => C.DevPlay(p, 1)));
                var h = Ui.Btn("Pause", null, () => C.DevPlay(p, 2)); h.Margin = new Thickness(6, 0, 0, 0);
                btns.Children.Add(h);
            }
            var d = Ui.Btn("Disconnect", W.SymbolRegular.LinkDismiss24, async () =>
            {
                var extra = pc ? "\n\nThis may be the current PC; disconnecting it will also disconnect EarKit." : "";
                if (await Ui.Confirm("Disconnect device", $"Disconnect the headset from “{p.Name}”?{extra}", "Disconnect")) C.DevDisconnect(p);
            });
            d.Margin = new Thickness(6, 0, 0, 0);
            btns.Children.Add(d);
        }
        else
        {
            var b = Ui.Btn("Connect", W.SymbolRegular.Link24, () => C.DevConnect(p), W.ControlAppearance.Primary);
            b.IsEnabled = C.ConnectedPhones().Count < 2;
            btns.Children.Add(b);
        }
        Grid.SetColumn(btns, 2); g.Children.Add(btns);
        return Ui.Panel(g, "Row");
    }

    protected override void OnState(StateUpdate s) { if (s.ContainsKey("dual") || s.ContainsKey("ldac")) OnAll(); }

    protected override void OnAll()
    {
        IsEnabled = Connected;
        var cfg = C.Dual;
        bool on = C.Get<bool>("dual");
        _dual.IsChecked = on;
        var tips = new List<string>();
        if (!on) tips.Add("Multipoint is off. Turn it on to connect two devices and view or switch between them below.");
        if (cfg.Ldac) tips.Add("Multipoint is unavailable while LDAC is enabled.");
        if (cfg.OpenReset || cfg.ToggleReset) tips.Add("The headset restarts after this setting changes. EarKit reconnects in about 20 seconds.");
        if (cfg.Resend) tips.Add("Connect and disconnect commands are resent every 3 seconds, up to three times, as in the official app. Wait for the list to refresh.");
        if (!cfg.List) tips.Add("This model reports its device list automatically. If the list is empty, click Refresh list.");
        _tips.Message = string.Join("\n", tips);
        Ui.Show(_body, on);

        var host = Environment.MachineName.ToLowerInvariant();
        var con = C.ConnectedPhones().OrderBy(p => !DeviceController.IsThisPc(p)).ThenBy(p => p.Num).ToList();
        var off = C.Phones.Values.Where(p => !p.Connected).OrderBy(p => p.Num).ToList();
        _count.Text = $"{con.Count}/2";
        _on.Children.Clear();
        foreach (var p in con) _on.Children.Add(Card(p, true));
        if (con.Count == 0) _on.Children.Add(Ui.Panel(Ui.T("No data yet. Click Refresh list to try again.", "RowDesc"), "Row"));
        if (con.Count >= 2)
            _on.Children.Add(Ui.T("Two devices are connected. Disconnect one before connecting another.", "Hint"));
        _off.Children.Clear();
        foreach (var p in off) _off.Children.Add(Card(p, false));
        Ui.Show(_offBox, cfg.ShowOff && off.Count > 0);

        _newBtn.IsEnabled = con.Count < 2 && !C.Pairing;
        Ui.Show(_newBtn, !C.Pairing);
        Ui.Show(_pairPanel, C.Pairing);
        var name = C.Model?.Name ?? "Headset";
        _pairText.Text = $"The headset is in pairing mode. Find and connect to “{name}” in Bluetooth settings on your new device (phone, tablet, or computer). Pairing ends automatically after connection.\n{Math.Max(0, C.PairingLeft)} seconds remaining";
    }
}
