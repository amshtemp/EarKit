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
        Root.Children.Add(Ui.Header("Headset Settings", "Adjust general headset settings. Available features vary by model; only supported options are shown."));

        _ear = Ui.Toggle(User<bool>(v => C.SetEar(v)));
        _rEar = Ui.Row(W.SymbolRegular.Headphones24, "In-ear detection", "Automatically pause playback when you remove an earbud", _ear);

        _pvol = Ui.Combo(Catalog.PROMPT_VOL.Select(v => v == 0 ? "Mute" : v + "%"), 140);
        _pvol.SelectionChanged += (_, _) => { if (!Sync && _pvol.SelectedIndex >= 0) C.SetPrompt(Catalog.PROMPT_VOL[_pvol.SelectedIndex]); };
        _rPvol = Ui.Row(W.SymbolRegular.Speaker224, "Prompt volume", "Volume of voice prompts for power, connection, and other events", _pvol);

        _ao = Ui.Combo(Array.Empty<string>(), 140);
        _ao.SelectionChanged += (_, _) => { if (!Sync && _ao.SelectedIndex >= 0 && _ao.SelectedIndex < _aoList.Count) C.SetAutoOff(_aoList[_ao.SelectedIndex]); };
        _rAo = Ui.Row(W.SymbolRegular.Power24, "Auto power-off", "Turn off automatically after remaining disconnected for a while", _ao);

        _mic = Ui.Combo(new[] { "Automatic", "Left earbud only", "Right earbud only" }, 140);
        _mic.SelectionChanged += (_, _) => { if (!Sync && _mic.SelectedIndex >= 0) C.SetMic(new[] { "AUTO", "L", "R" }[_mic.SelectedIndex]); };
        _rMic = Ui.Row(W.SymbolRegular.Mic24, "Call microphone", "Choose which microphone to use during calls", _mic);

        var q = new WrapPanel();
        q.Children.Add(_ldac); q.Children.Add(_aptx); q.Children.Add(_adapt); q.Children.Add(_lossless);
        var apply = Ui.Btn("Apply", W.SymbolRegular.Save24, async () =>
        {
            if (_ldac.IsChecked == true && C.Get<bool>("dual") && C.Dual.Ldac &&
                !await Ui.Confirm("LDAC conflicts with multipoint", "This model cannot use multipoint while LDAC is enabled, and the headset may turn multipoint off automatically. Continue?", "Continue")) return;
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
        _rQual = Ui.Row(W.SymbolRegular.SoundWaveCircle24, "High-quality audio codecs", "The headset may briefly disconnect and reconnect after a change. The codec used also depends on support from your computer or phone.", null, qp);

        _findL = Ui.Toggle(User<bool>(_ => C.SetFind(_findL!.IsChecked == true, _findR!.IsChecked == true)));
        _findR = Ui.Toggle(User<bool>(_ => C.SetFind(_findL!.IsChecked == true, _findR!.IsChecked == true)));
        var fp = new StackPanel { Orientation = Orientation.Horizontal };
        fp.Children.Add(new TextBlock { Text = "Ring left", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        fp.Children.Add(_findL);
        fp.Children.Add(new TextBlock { Text = "Ring right", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(24, 0, 8, 0) });
        fp.Children.Add(_findR);
        _rFind = Ui.Row(W.SymbolRegular.Search24, "Find headset", "The headset will play a tone. Do not wear it while ringing.", fp);

        var reset = Ui.Btn("Factory reset…", W.SymbolRegular.ArrowReset24, async () =>
        {
            if (await Ui.Confirm("Factory reset", "Are you sure you want to reset the headset?\nIt will disconnect and erase all pairings and settings.", "Reset", danger: true))
                C.ResetFactory();
        }, W.ControlAppearance.Danger);
        _rReset = Ui.Row(W.SymbolRegular.Warning24, "Factory reset", "Erase all headset settings and pairing records", reset);

        Root.Children.Add(Ui.Section("General"));
        foreach (var r in new[] { _rEar, _rPvol, _rAo, _rMic }) Root.Children.Add(r);
        Root.Children.Add(Ui.Section("Audio quality"));
        Root.Children.Add(_rQual);
        Root.Children.Add(Ui.Section("Other"));
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
        if (has("codec")) _codecNow.Text = C.TryGet<string>("codec", out var c) ? $"Current codec: {c}" : "";
    }
}
