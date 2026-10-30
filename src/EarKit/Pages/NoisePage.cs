using System.Windows;
using System.Windows.Controls;
using EarKit.Controls;
using EarKit.Core;
namespace EarKit.Pages;

public sealed class NoisePage : PageBase
{
    readonly WrapPanel _modes = new();
    readonly StackPanel _subBox = new(), _ambBox = new();
    readonly WrapPanel _subs = new(), _ambs = new();
    readonly Dictionary<string, RadioButton> _mb = new(), _sb = new(), _ab = new();

    public NoisePage()
    {
        Root.Children.Add(Ui.Header("Noise Control", "Switch between noise cancellation and ambient modes, then adjust their levels."));
        Root.Children.Add(Ui.Section("Mode"));
        Root.Children.Add(_modes);
        _subBox.Children.Add(Ui.Section("Noise cancellation level"));
        _subBox.Children.Add(Ui.T("Applies in Noise Cancellation mode.", "Hint"));
        _subs.Margin = new Thickness(0, 8, 0, 0);
        _subBox.Children.Add(_subs);
        _ambBox.Children.Add(Ui.Section("Ambient level"));
        _ambBox.Children.Add(Ui.T("Applies in Ambient mode.", "Hint"));
        _ambs.Margin = new Thickness(0, 8, 0, 0);
        _ambBox.Children.Add(_ambs);
        Root.Children.Add(_subBox);
        Root.Children.Add(_ambBox);
        Guard(() => { OnModel(); OnAll(); });
    }

    protected override void OnModel()
    {
        _modes.Children.Clear(); _subs.Children.Clear(); _ambs.Children.Clear();
        _mb.Clear(); _sb.Clear(); _ab.Clear();
        var m = C.Model;
        if (m == null) return;
        foreach (var n in m.Noise)
        {
            var k = n;
            var rb = Ui.ModeCard("np_noise", Ui.NoiseIcon(k), Catalog.NOISE_LABEL.GetValueOrDefault(k, k), Ui.NoiseDesc(k), 170);
            rb.MinHeight = 104;
            rb.Checked += (_, _) => { if (!Sync) C.SetNoise(k); };
            _mb[k] = rb; _modes.Children.Add(rb);
        }
        foreach (var s in m.Sub)
        {
            var k = s;
            var rb = Ui.Pill("np_sub", Catalog.SUB_LABEL.GetValueOrDefault(k, k));
            rb.Checked += (_, _) => { if (!Sync) C.SetSub(k); };
            _sb[k] = rb; _subs.Children.Add(rb);
        }
        foreach (var a in m.Amb)
        {
            var k = a;
            var rb = Ui.Pill("np_amb", Catalog.AMB_LABEL.GetValueOrDefault(k, k));
            rb.Checked += (_, _) => { if (!Sync) C.SetAmb(k); };
            _ab[k] = rb; _ambs.Children.Add(rb);
        }
    }

    protected override void OnAll()
    {
        var m = C.Model;
        IsEnabled = Connected;
        Ui.Show(_subBox, m != null && m.Sub.Count > 0);
        Ui.Show(_ambBox, m != null && m.Amb.Count > 0);
        var n = C.Get<string>("noise"); var s = C.Get<string>("sub"); var a = C.Get<string>("amb");
        foreach (var kv in _mb) kv.Value.IsChecked = kv.Key == n;
        foreach (var kv in _sb) kv.Value.IsChecked = kv.Key == s;
        foreach (var kv in _ab) kv.Value.IsChecked = kv.Key == a;
    }
}
