using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using EarKit.Controls;
using EarKit.Core;
using W = Wpf.Ui.Controls;
namespace EarKit.Pages;

public sealed class OverviewPage : PageBase
{
    readonly TextBlock _name, _status;
    readonly WrapPanel _tags = new() { Margin = new Thickness(0, 10, 0, 0) };
    readonly StackPanel _rings = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    readonly RingGauge _rl = Ring("左耳"), _rr = Ring("右耳"), _rb = Ring("充电盒");
    readonly StackPanel _offline = new();
    readonly StackPanel _noiseBox = new();
    readonly WrapPanel _noiseCards = new();
    readonly Dictionary<string, RadioButton> _noiseBtns = new();
    readonly StackPanel _quick = new();
    readonly W.ToggleSwitch _game, _spatial;
    readonly Border _gameRow, _spatialRow;

    static RingGauge Ring(string label)
    {
        var r = new RingGauge { Label = label, Width = 116, Height = 140, Margin = new Thickness(8, 0, 8, 0) };
        r.SetResourceReference(RingGauge.TextBrushProperty, "TextFillColorPrimaryBrush");
        r.SetResourceReference(RingGauge.SubBrushProperty, "TextFillColorSecondaryBrush");
        return r;
    }

    public OverviewPage()
    {
        // ---------- 顶部卡片
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var img = new Image { Width = 72, Height = 72, Margin = new Thickness(4, 0, 20, 0), VerticalAlignment = VerticalAlignment.Center,
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/app.png")) };
        g.Children.Add(img);
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        _name = Ui.T("未连接耳机"); _name.FontSize = 24; _name.FontWeight = FontWeights.SemiBold;
        Ui.Fg(_name, "TextFillColorPrimaryBrush");
        _status = Ui.T("", "RowDesc");
        info.Children.Add(_name); info.Children.Add(_status); info.Children.Add(_tags);
        Grid.SetColumn(info, 1); g.Children.Add(info);
        _rings.Children.Add(_rl); _rings.Children.Add(_rr); _rings.Children.Add(_rb);
        Grid.SetColumn(_rings, 2); g.Children.Add(_rings);
        var hero = new StackPanel();
        hero.Children.Add(g);
        _offline.Margin = new Thickness(0, 16, 0, 0);
        _offline.Children.Add(Ui.T("请先在 Windows“设置 → 蓝牙和设备”中配对并连接耳机，然后在上方选择设备并点击“连接”。" +
            "如果手机上的 EarFun App 正在连接耳机，请先在手机上关闭 App 或断开蓝牙，否则可能连接失败。", "RowDesc"));
        hero.Children.Add(_offline);
        Root.Children.Add(Ui.Panel(hero, "Panel", new Thickness(0, 0, 0, 4)));

        // ---------- 降噪
        _noiseBox.Children.Add(Ui.Section("降噪模式"));
        _noiseBox.Children.Add(_noiseCards);
        Root.Children.Add(_noiseBox);

        // ---------- 快捷开关
        _quick.Children.Add(Ui.Section("快捷开关"));
        _game = Ui.Toggle(User<bool>(v => C.SetGame(v)));
        _gameRow = Ui.Row(W.SymbolRegular.Games24, "游戏模式", "降低音频延迟，适合游戏和看视频", _game);
        _spatial = Ui.Toggle(User<bool>(v => C.SetSpatial(v)));
        _spatialRow = Ui.Row(W.SymbolRegular.Sparkle24, "空间音频", "营造环绕声场效果", _spatial);
        _quick.Children.Add(_gameRow); _quick.Children.Add(_spatialRow);
        Root.Children.Add(_quick);
        Guard(() => { OnModel(); OnAll(); });
    }

    protected override void OnModel()
    {
        _noiseCards.Children.Clear(); _noiseBtns.Clear();
        var m = C.Model;
        if (m == null) return;
        foreach (var n in m.Noise)
        {
            var k = n;
            var rb = Ui.ModeCard("ov_noise", Ui.NoiseIcon(k), Catalog.NOISE_LABEL.GetValueOrDefault(k, k), Ui.NoiseDesc(k), 150);
            rb.Checked += (_, _) => { if (!Sync) C.SetNoise(k); };
            _noiseBtns[k] = rb; _noiseCards.Children.Add(rb);
        }
        _rl.Label = m.IsHead ? "耳机" : "左耳";
    }

    static int Bat(string k) => C.TryGet<int>(k, out var v) ? v : -1;

    protected override void OnAll()
    {
        var m = C.Model;
        bool on = Connected;
        Ui.Show(_offline, !on);
        Ui.Show(_rings, on);
        _tags.Children.Clear();
        if (!on || m == null)
        {
            _name.Text = C.Conn == Services.ConnState.Connecting ? "正在连接…" : "未连接耳机";
            _status.Text = C.StatusText == "未连接" ? "EarKit 可以控制 EarFun 耳机的降噪、均衡器、按键、多设备连接等功能" : C.StatusText;
            Ui.Show(_noiseBox, false); Ui.Show(_quick, false);
            return;
        }
        _name.Text = m.Name;
        _status.Text = C.StatusText;
        void Tag(string t)
        {
            var b = new Border { Child = new TextBlock { Text = t, FontSize = 12 } };
            b.SetResourceReference(StyleProperty, "Tag");
            _tags.Children.Add(b);
        }
        Tag(C.ProtocolName ?? "");
        if (C.Firmware != null) Tag("固件 " + C.Firmware);
        if (C.TryGet<string>("codec", out var codec)) Tag("编码 " + codec);
        if (C.UnlockAll) Tag("已解锁全部功能");

        _rl.Value = Bat("bat_l"); _rl.Charging = C.Get<bool>("chg_l");
        _rr.Value = Bat("bat_r"); _rr.Charging = C.Get<bool>("chg_r");
        _rb.Value = Bat("bat_box"); _rb.Charging = C.Get<bool>("box_charging");
        Ui.Show(_rr, !m.IsHead); Ui.Show(_rb, !m.IsHead);
        if ((uint)_rl.Value > 100 && !m.IsHead) _rl.Label = "左耳（不在线）"; else _rl.Label = m.IsHead ? "耳机" : "左耳";
        _rr.Label = (uint)_rr.Value > 100 ? "右耳（不在线）" : "右耳";

        Ui.Show(_noiseBox, C.Has("noise") && m.Noise.Count > 0);
        var cur = C.Get<string>("noise");
        foreach (var kv in _noiseBtns) kv.Value.IsChecked = kv.Key == cur;
        Ui.Show(_gameRow, C.Has("game")); Ui.Show(_spatialRow, C.Has("spatial"));
        Ui.Show(_quick, C.Has("game") || C.Has("spatial"));
        _game.IsChecked = C.Get<bool>("game");
        _spatial.IsChecked = C.Get<bool>("spatial");
    }
}
