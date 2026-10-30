using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using EarKit.Controls;
using EarKit.Core;
using W = Wpf.Ui.Controls;
namespace EarKit.Pages;

public sealed class KeysPage : PageBase
{
    readonly UniformGrid _grid = new() { Rows = 1 };
    readonly Dictionary<string, ComboBox> _cbs = new();
    readonly StackPanel _disBox = new();
    readonly W.ToggleSwitch _disL, _disR;
    List<string> _ops = new();

    public KeysPage()
    {
        var head = Ui.Header("Controls", "Customize the headset's touch gestures. Changes are written to the headset immediately.");
        Root.Children.Add(head);
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(Ui.Btn("Read from headset", W.SymbolRegular.ArrowSync24, () => C.ReadKeys()));
        Root.Children.Add(bar);
        Root.Children.Add(_grid);

        _disBox.Children.Add(Ui.Section("Disable touch controls"));
        _disL = Ui.Toggle(User<bool>(_ => C.SetDisableKeys(_disL!.IsChecked == true, _disR!.IsChecked == true)));
        _disR = Ui.Toggle(User<bool>(_ => C.SetDisableKeys(_disL!.IsChecked == true, _disR!.IsChecked == true)));
        _disBox.Children.Add(Ui.Row(W.SymbolRegular.Dismiss24, "Disable left earbud controls", "Prevent accidental touches while wearing or adjusting the earbud", _disL));
        _disBox.Children.Add(Ui.Row(W.SymbolRegular.Dismiss24, "Disable right earbud controls", "Prevent accidental touches while wearing or adjusting the earbud", _disR));
        Root.Children.Add(_disBox);
        Guard(() => { OnModel(); OnAll(); });
    }

    protected override void OnModel()
    {
        _grid.Children.Clear(); _cbs.Clear();
        var m = C.Model;
        if (m == null) return;
        _ops = m.KeyOps;
        bool head = !m.IsQcc && m.Variant == "head";
        var sides = head ? new[] { ("PLUS", "Volume + button"), ("MINUS", "Volume - button"), ("NC", "Noise control button") } : new[] { ("L", "Left earbud"), ("R", "Right earbud") };
        var gests = head ? new[] { ("ONE", "Single press"), ("LONG", "Long press") } : Catalog.GESTURES;
        _grid.Columns = sides.Length;
        foreach (var (sk, st) in sides)
        {
            var sp = new StackPanel();
            var t = Ui.T(st, "SectionTitle"); t.Margin = new Thickness(0, 0, 0, 10);
            sp.Children.Add(t);
            foreach (var (gk, gt) in gests)
            {
                var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var l = Ui.T(gt, "RowTitle"); l.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(l);
                var cb = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
                foreach (var o in _ops) cb.Items.Add(Catalog.OpLabel(o));
                string side = sk, g = gk;
                cb.SelectionChanged += (_, _) =>
                {
                    if (Sync || cb.SelectedIndex < 0 || cb.SelectedIndex >= _ops.Count) return;
                    C.SetKey(side, g, _ops[cb.SelectedIndex]);
                };
                Grid.SetColumn(cb, 1); row.Children.Add(cb);
                _cbs[Proto.KK(sk, gk)] = cb;
                sp.Children.Add(row);
            }
            _grid.Children.Add(Ui.Panel(sp, "Panel", new Thickness(0, 0, 8, 8)));
        }
    }

    protected override void OnAll()
    {
        var m = C.Model;
        IsEnabled = Connected;
        Ui.Show(_disBox, m != null && m.Battery == "tws");
        var keys = C.Get<Dictionary<string, string>>("keys");
        if (keys != null)
            foreach (var kv in keys)
            {
                if (!_cbs.TryGetValue(kv.Key, out var cb)) continue;
                int i = _ops.IndexOf(kv.Value);
                if (i >= 0) { RemoveExtra(cb); cb.SelectedIndex = i; }
                else
                {
                    RemoveExtra(cb);
                    cb.Items.Add(Catalog.OpLabel(kv.Value) + " (current)");
                    cb.SelectedIndex = cb.Items.Count - 1;
                }
            }
        _disL.IsChecked = C.Get<bool>("dis_l");
        _disR.IsChecked = C.Get<bool>("dis_r");
    }

    void RemoveExtra(ComboBox cb) { while (cb.Items.Count > _ops.Count) cb.Items.RemoveAt(cb.Items.Count - 1); }
}
