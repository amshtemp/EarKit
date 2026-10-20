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
    readonly W.InfoBar _tips = new() { IsOpen = true, IsClosable = false, Severity = W.InfoBarSeverity.Informational, Title = "提示", Margin = new Thickness(0, 8, 0, 0) };
    readonly StackPanel _body = new();
    readonly TextBlock _count;
    readonly StackPanel _on = new(), _off = new(), _offBox = new();
    readonly W.Button _newBtn;
    readonly Border _pairPanel;
    readonly TextBlock _pairText;

    public MultiPage()
    {
        Root.Children.Add(Ui.Header("多设备连接", "让耳机同时连接两台设备（如手机 + 电脑），并在这里查看、切换耳机连接的设备。"));
        _dual = Ui.Toggle(User<bool>(OnToggle));
        Root.Children.Add(Ui.Row(W.SymbolRegular.PhoneLaptop24, "多设备连接", "同时连接两台设备，来电或播放时自动切换", _dual));
        Root.Children.Add(_tips);

        var hdr = new Grid { Margin = new Thickness(0, 18, 0, 8) };
        hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        var t = Ui.T("已连接的设备", "SectionTitle"); t.Margin = new Thickness(2, 0, 10, 0); t.VerticalAlignment = VerticalAlignment.Center;
        titleRow.Children.Add(t);
        _count = new TextBlock { Text = "0/2", FontSize = 12, FontWeight = FontWeights.SemiBold };
        var badge = new Border { Child = _count, VerticalAlignment = VerticalAlignment.Center };
        badge.SetResourceReference(StyleProperty, "Tag");
        titleRow.Children.Add(badge);
        hdr.Children.Add(titleRow);
        var refresh = Ui.Btn("刷新列表", W.SymbolRegular.ArrowSync24, () => C.RefreshDevList());
        Grid.SetColumn(refresh, 1); hdr.Children.Add(refresh);
        _body.Children.Add(hdr);
        _body.Children.Add(_on);

        _offBox.Children.Add(Ui.Section("已配对但未连接的设备"));
        _offBox.Children.Add(_off);
        _body.Children.Add(_offBox);

        _body.Children.Add(Ui.Section("连接新设备"));
        _newBtn = Ui.Btn("让耳机进入配对模式…", W.SymbolRegular.Add24, () => C.StartPairing(), W.ControlAppearance.Primary);
        var stop = Ui.Btn("停止配对", W.SymbolRegular.Dismiss24, () => C.StopPairing(false));
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
            if (!await Ui.Confirm("LDAC 与多设备连接冲突", "当前已开启 LDAC。LDAC 与多设备连接不能同时使用（官方 App 也会拦截）。\n\n是否先关闭 LDAC，再开启多设备连接？", "关闭 LDAC 并开启"))
            {
                Guard(() => _dual.IsChecked = false);
                return;
            }
            offLdac = true;
        }
        var cfg = C.Dual;
        if ((on && cfg.OpenReset) || cfg.ToggleReset)
        {
            if (!await Ui.Confirm("耳机将会重启", $"该型号{(on ? "开启" : "切换")}多设备连接后会自动重启以生效，期间连接会断开，约 20 秒后本程序会自动重连。\n\n是否继续？", "继续"))
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
        info.Children.Add(Ui.T(string.IsNullOrEmpty(p.Name) ? "(未知设备)" : p.Name, "RowTitle"));
        var st = connected ? "已连接" : "未连接";
        if (connected && C.Plays.TryGetValue(p.Mac, out var pl)) st += pl == 1 ? " · 播放中" : pl == 2 ? " · 已挂起" : "";
        if (pc) st += " · 可能是本机";
        info.Children.Add(Ui.T($"{st}   ·   地址 …{p.Mac}", "RowDesc"));
        Grid.SetColumn(info, 1); g.Children.Add(info);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (connected)
        {
            if (C.Dual.Play)
            {
                btns.Children.Add(Ui.Btn("设为播放", W.SymbolRegular.Play24, () => C.DevPlay(p, 1)));
                var h = Ui.Btn("挂起", null, () => C.DevPlay(p, 2)); h.Margin = new Thickness(6, 0, 0, 0);
                btns.Children.Add(h);
            }
            var d = Ui.Btn("断开", W.SymbolRegular.LinkDismiss24, async () =>
            {
                var extra = pc ? "\n\n注意：这可能就是本电脑，断开后本程序也会掉线。" : "";
                if (await Ui.Confirm("断开设备", $"确定断开耳机与“{p.Name}”的连接吗？{extra}", "断开")) C.DevDisconnect(p);
            });
            d.Margin = new Thickness(6, 0, 0, 0);
            btns.Children.Add(d);
        }
        else
        {
            var b = Ui.Btn("连接", W.SymbolRegular.Link24, () => C.DevConnect(p), W.ControlAppearance.Primary);
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
        if (!on) tips.Add("多设备连接已关闭。开启后可同时连接两台设备，并在下方查看 / 切换耳机连接的设备。");
        if (cfg.Ldac) tips.Add("LDAC 开启时无法使用多设备连接。");
        if (cfg.OpenReset || cfg.ToggleReset) tips.Add("切换该设置后耳机会自动重启，程序会在约 20 秒后自动重连。");
        if (cfg.Resend) tips.Add("连接 / 断开命令会按官方 App 的方式每 3 秒重发一次（共 3 次），请稍等列表刷新。");
        if (!cfg.List) tips.Add("该型号由耳机主动上报设备列表，若列表为空可点“刷新列表”。");
        _tips.Message = string.Join("\n", tips);
        Ui.Show(_body, on);

        var host = Environment.MachineName.ToLowerInvariant();
        var con = C.ConnectedPhones().OrderBy(p => !DeviceController.IsThisPc(p)).ThenBy(p => p.Num).ToList();
        var off = C.Phones.Values.Where(p => !p.Connected).OrderBy(p => p.Num).ToList();
        _count.Text = $"{con.Count}/2";
        _on.Children.Clear();
        foreach (var p in con) _on.Children.Add(Card(p, true));
        if (con.Count == 0) _on.Children.Add(Ui.Panel(Ui.T("暂无数据，点击“刷新列表”重新读取。", "RowDesc"), "Row"));
        if (con.Count >= 2)
            _on.Children.Add(Ui.T("已连接 2 台设备（已满），需先断开一台才能连接其他设备。", "Hint"));
        _off.Children.Clear();
        foreach (var p in off) _off.Children.Add(Card(p, false));
        Ui.Show(_offBox, cfg.ShowOff && off.Count > 0);

        _newBtn.IsEnabled = con.Count < 2 && !C.Pairing;
        Ui.Show(_newBtn, !C.Pairing);
        Ui.Show(_pairPanel, C.Pairing);
        var name = C.Model?.Name ?? "耳机";
        _pairText.Text = $"耳机已进入配对模式。请在新设备（手机 / 平板 / 电脑）的蓝牙设置中搜索并连接“{name}”。连接成功后会自动结束。\n剩余 {Math.Max(0, C.PairingLeft)} 秒";
    }
}
