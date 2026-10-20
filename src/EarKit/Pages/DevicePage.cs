using System.Windows;
using System.Windows.Controls;
using EarKit.Controls;
using EarKit.Core;
using W = Wpf.Ui.Controls;
namespace EarKit.Pages;

public sealed class DevicePage : PageBase
{
    readonly W.ToggleSwitch _ear;
    readonly ComboBox _pvol, _ao, _mic;
    readonly CheckBox _ldac = new() { Content = "LDAC", Margin = new Thickness(0, 0, 20, 0) },
        _aptx = new() { Content = "aptX", Margin = new Thickness(0, 0, 20, 0) },
        _adapt = new() { Content = "aptX Adaptive", Margin = new Thickness(0, 0, 20, 0) },
        _lossless = new() { Content = "aptX Lossless", Margin = new Thickness(0, 0, 20, 0) };
    readonly W.ToggleSwitch _findL, _findR;
    readonly Border _rEar, _rPvol, _rAo, _rMic, _rQual, _rFind, _rReset;
    readonly TextBlock _codecNow;
    List<int> _aoList = new();

    public DevicePage()
    {
        Root.Children.Add(Ui.Header("耳机设置", "调整耳机的常规设置。不同型号支持的功能不同，这里只显示当前型号支持的项目。"));

        _ear = Ui.Toggle(User<bool>(v => C.SetEar(v)));
        _rEar = Ui.Row(W.SymbolRegular.Headphones24, "入耳检测", "摘下耳机时自动暂停播放", _ear);

        _pvol = Ui.Combo(Catalog.PROMPT_VOL.Select(v => v == 0 ? "静音" : v + "%"), 140);
        _pvol.SelectionChanged += (_, _) => { if (!Sync && _pvol.SelectedIndex >= 0) C.SetPrompt(Catalog.PROMPT_VOL[_pvol.SelectedIndex]); };
        _rPvol = Ui.Row(W.SymbolRegular.Speaker224, "提示音音量", "开关机、连接等语音提示的音量", _pvol);

        _ao = Ui.Combo(Array.Empty<string>(), 140);
        _ao.SelectionChanged += (_, _) => { if (!Sync && _ao.SelectedIndex >= 0 && _ao.SelectedIndex < _aoList.Count) C.SetAutoOff(_aoList[_ao.SelectedIndex]); };
        _rAo = Ui.Row(W.SymbolRegular.Power24, "自动关机", "无连接一段时间后自动关机", _ao);

        _mic = Ui.Combo(new[] { "自动", "仅左耳", "仅右耳" }, 140);
        _mic.SelectionChanged += (_, _) => { if (!Sync && _mic.SelectedIndex >= 0) C.SetMic(new[] { "AUTO", "L", "R" }[_mic.SelectedIndex]); };
        _rMic = Ui.Row(W.SymbolRegular.Mic24, "通话麦克风", "选择通话时使用的麦克风", _mic);

        var q = new WrapPanel();
        q.Children.Add(_ldac); q.Children.Add(_aptx); q.Children.Add(_adapt); q.Children.Add(_lossless);
        var apply = Ui.Btn("应用", W.SymbolRegular.Save24, async () =>
        {
            if (_ldac.IsChecked == true && C.Get<bool>("dual") && C.Dual.Ldac &&
                !await Ui.Confirm("LDAC 与多设备连接冲突", "开启 LDAC 时该型号无法使用多设备连接，耳机可能会自动关闭多设备连接。是否继续？", "继续")) return;
            C.SetQuality(_ldac.IsChecked == true, _aptx.IsChecked == true, _adapt.IsChecked == true, _lossless.IsChecked == true);
        }, W.ControlAppearance.Primary);
        _codecNow = Ui.T("", "Hint");
        var qp = new StackPanel();
        qp.Children.Add(q);
        var qb = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        qb.Children.Add(apply);
        _codecNow.VerticalAlignment = VerticalAlignment.Center; _codecNow.Margin = new Thickness(12, 0, 0, 0);
        qb.Children.Add(_codecNow);
        qp.Children.Add(qb);
        _rQual = Ui.Row(W.SymbolRegular.SoundWaveCircle24, "高清音频编码", "改动后耳机可能会短暂断开重连。实际使用的编码还取决于电脑/手机是否支持。", null, qp);

        _findL = Ui.Toggle(User<bool>(_ => C.SetFind(_findL!.IsChecked == true, _findR!.IsChecked == true)));
        _findR = Ui.Toggle(User<bool>(_ => C.SetFind(_findL!.IsChecked == true, _findR!.IsChecked == true)));
        var fp = new StackPanel { Orientation = Orientation.Horizontal };
        fp.Children.Add(new TextBlock { Text = "左耳响铃", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        fp.Children.Add(_findL);
        fp.Children.Add(new TextBlock { Text = "右耳响铃", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 8, 0) });
        fp.Children.Add(_findR);
        _rFind = Ui.Row(W.SymbolRegular.Search24, "查找耳机", "耳机会发出提示音，请勿佩戴！", fp);

        var reset = Ui.Btn("恢复出厂设置…", W.SymbolRegular.ArrowReset24, async () =>
        {
            if (await Ui.Confirm("恢复出厂设置", "确定要把耳机恢复出厂设置吗？\n耳机会断开并清除配对和所有设置。", "恢复出厂", danger: true))
                C.ResetFactory();
        }, W.ControlAppearance.Danger);
        _rReset = Ui.Row(W.SymbolRegular.Warning24, "恢复出厂设置", "清除耳机的所有设置和配对记录", reset);

        Root.Children.Add(Ui.Section("常规"));
        foreach (var r in new[] { _rEar, _rPvol, _rAo, _rMic }) Root.Children.Add(r);
        Root.Children.Add(Ui.Section("音质"));
        Root.Children.Add(_rQual);
        Root.Children.Add(Ui.Section("其他"));
        Root.Children.Add(_rFind);
        Root.Children.Add(_rReset);
        Guard(() => { OnModel(); OnAll(); });
    }

    protected override void OnModel()
    {
        var m = C.Model;
        _aoList = (m?.AutoOff.Count > 0 ? m.AutoOff : Catalog.AO6.ToList());
        _ao.Items.Clear();
        foreach (var a in _aoList) _ao.Items.Add(Catalog.AutoOffLabel(a));
        Ui.Show(_lossless, m?.IsQcc == true);
    }

    protected override void OnAll() => Apply(_ => true);
    protected override void OnState(StateUpdate s) => Apply(s.ContainsKey);

    void Apply(Func<string, bool> has)
    {
        IsEnabled = Connected;
        Ui.Show(_rEar, C.Has("ear")); Ui.Show(_rPvol, C.Has("prompt")); Ui.Show(_rAo, C.Has("autooff"));
        Ui.Show(_rMic, C.Has("mic")); Ui.Show(_rQual, C.Has("quality")); Ui.Show(_rFind, C.Has("find")); Ui.Show(_rReset, C.Has("reset"));
        if (has("ear")) _ear.IsChecked = C.Get<bool>("ear");
        if (has("prompt") && C.TryGet<int>("prompt", out var pv)) _pvol.SelectedIndex = Array.IndexOf(Catalog.PROMPT_VOL, pv);
        if (has("autooff") && C.TryGet<int>("autooff", out var ao)) _ao.SelectedIndex = _aoList.IndexOf(ao);
        if (has("mic") && C.TryGet<string>("mic", out var mic)) _mic.SelectedIndex = Array.IndexOf(new[] { "AUTO", "L", "R" }, mic);
        if (has("ldac") || has("aptx") || has("adaptive") || has("lossless"))
        {
            _ldac.IsChecked = C.Get<bool>("ldac"); _aptx.IsChecked = C.Get<bool>("aptx");
            _adapt.IsChecked = C.Get<bool>("adaptive"); _lossless.IsChecked = C.Get<bool>("lossless");
        }
        if (has("find_l") || has("find_r")) { _findL.IsChecked = C.Get<bool>("find_l"); _findR.IsChecked = C.Get<bool>("find_r"); }
        if (has("codec")) _codecNow.Text = C.TryGet<string>("codec", out var c) ? $"当前编码：{c}" : "";
    }
}
