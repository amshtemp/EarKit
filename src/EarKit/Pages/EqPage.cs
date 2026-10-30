using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using EarKit.Controls;
using EarKit.Core;
using EarKit.Services;
using W = Wpf.Ui.Controls;
namespace EarKit.Pages;

public sealed class EqPage : PageBase
{
    readonly ComboBox _preset = new() { MinWidth = 220 };
    readonly EqCurve _curve = new() { Height = 190 };
    readonly UniformGrid _bands = new() { Rows = 1 };
    readonly W.ToggleSwitch _auto;
    readonly W.Button _del;
    readonly List<Slider> _sl = new();
    readonly List<TextBlock> _vl = new();
    readonly DispatcherTimer _deb = new() { Interval = TimeSpan.FromMilliseconds(450) };
    int _n = 10;
    bool _dragging;

    static readonly string[] L10 = { "31", "63", "125", "250", "500", "1K", "2K", "4K", "8K", "16K" };
    static readonly string[] L6 = { "Sub-bass", "Bass", "Midrange", "Upper midrange", "Treble", "Ultra-treble" };

    public EqPage()
    {
        _curve.SetResourceReference(EqCurve.AccentProperty, "SystemAccentColorPrimaryBrush");
        _curve.SetResourceReference(EqCurve.TextBrushProperty, "TextFillColorTertiaryBrush");
        Root.Children.Add(Ui.Header("Equalizer", "Choose a preset or adjust the frequency bands manually. The headset does not report EQ settings, so this shows the last values set by EarKit for this model."));

        var bar = new Grid();
        for (int i = 0; i < 6; i++) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 4 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var lbl = Ui.T("Preset", "RowTitle"); lbl.VerticalAlignment = VerticalAlignment.Center; lbl.Margin = new Thickness(0, 0, 12, 0);
        bar.Children.Add(lbl);
        Grid.SetColumn(_preset, 1); bar.Children.Add(_preset);
        var save = Ui.Btn("Save preset", W.SymbolRegular.Save24, SavePreset); save.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(save, 2); bar.Children.Add(save);
        _del = Ui.Btn("Delete", W.SymbolRegular.Delete24, DeletePreset); _del.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_del, 3); bar.Children.Add(_del);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var al = Ui.T("Apply automatically while adjusting", "RowDesc"); al.VerticalAlignment = VerticalAlignment.Center; al.Margin = new Thickness(0, 0, 8, 0);
        _auto = new W.ToggleSwitch { IsChecked = S.EqAutoApply };
        _auto.Checked += (_, _) => { S.EqAutoApply = true; AppSettings.Save(); };
        _auto.Unchecked += (_, _) => { S.EqAutoApply = false; AppSettings.Save(); };
        right.Children.Add(al); right.Children.Add(_auto);
        var apply = Ui.Btn("Apply to headset", W.SymbolRegular.Play24, Send, W.ControlAppearance.Primary); apply.Margin = new Thickness(12, 0, 0, 0);
        right.Children.Add(apply);
        Grid.SetColumn(right, 4); Grid.SetColumnSpan(right, 2); bar.Children.Add(right);
        Root.Children.Add(Ui.Panel(bar, "Panel", new Thickness(0, 0, 0, 8)));

        Root.Children.Add(Ui.Panel(_curve, "Panel", new Thickness(0, 0, 0, 8)));
        Root.Children.Add(Ui.Panel(_bands));

        _preset.SelectionChanged += (_, _) => { if (!Sync) PickPreset(); };
        _deb.Tick += (_, _) => { _deb.Stop(); if (S.EqAutoApply) Send(); };
        Guard(OnModel);
    }

    protected override void OnModel()
    {
        var m = C.Model;
        _n = m?.EqBands ?? 10;
        Guard(() =>
        {
            _bands.Children.Clear(); _sl.Clear(); _vl.Clear();
            _bands.Columns = _n;
            for (int i = 0; i < _n; i++)
            {
                var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                var v = Ui.T("0 dB", "RowDesc"); v.HorizontalAlignment = HorizontalAlignment.Center; v.Margin = new Thickness(0, 0, 0, 6);
                var s = new Slider { Orientation = Orientation.Vertical, Minimum = -10, Maximum = 10, Height = 230, TickFrequency = 1,
                    IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 2, HorizontalAlignment = HorizontalAlignment.Center };
                s.ValueChanged += (_, _) => { UpdateLabels(); if (!Sync && !_dragging) { _deb.Stop(); _deb.Start(); } MarkCustom(); };
                s.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => _dragging = true));
                s.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { _dragging = false; if (S.EqAutoApply) Send(); }));
                var f = Ui.T(_n == 10 ? L10[i] : L6[i], "RowTitle"); f.HorizontalAlignment = HorizontalAlignment.Center; f.Margin = new Thickness(0, 8, 0, 0);
                col.Children.Add(v); col.Children.Add(s); col.Children.Add(f);
                _sl.Add(s); _vl.Add(v); _bands.Children.Add(col);
            }
            if (m != null && S.LastEq.TryGetValue(m.Key, out var last) && last.Length == _n)
                for (int i = 0; i < _n; i++) _sl[i].Value = last[i];
            FillPresets();
            UpdateLabels();
        });
    }

    void FillPresets(string? select = null)
    {
        _preset.Items.Clear();
        _preset.Items.Add("Custom");
        foreach (var p in _n == 10 ? Catalog.EQ_PRESETS_10 : Catalog.EQ_PRESETS_6) _preset.Items.Add(p.Name);
        foreach (var p in S.CustomPresets.Where(p => p.Bands == _n)) _preset.Items.Add("★ " + p.Name);
        var cur = Levels();
        string? match = select;
        if (match == null)
        {
            foreach (var p in S.CustomPresets.Where(p => p.Bands == _n)) if (p.Values.SequenceEqual(cur)) { match = "★ " + p.Name; break; }
            foreach (var p in _n == 10 ? Catalog.EQ_PRESETS_10 : Catalog.EQ_PRESETS_6) if (match == null && p.V.SequenceEqual(cur)) match = p.Name;
        }
        _preset.SelectedItem = match ?? "Custom";
        _del.IsEnabled = (_preset.SelectedItem as string)?.StartsWith("★ ") == true;
    }

    void MarkCustom()
    {
        if (Sync) return;
        Guard(() => { _preset.SelectedItem = "Custom"; _del.IsEnabled = false; });
    }

    int[] Levels() => _sl.Select(s => (int)Math.Round(s.Value)).ToArray();

    string Db(int lv) => _n == 10 ? $"{Catalog.GAINS[lv + 10]:+0.#;-0.#;0} dB" : $"{lv:+0;-0;0} dB";

    void UpdateLabels()
    {
        var lv = Levels();
        for (int i = 0; i < lv.Length && i < _vl.Count; i++) _vl[i].Text = Db(lv[i]);
        _curve.SetValues(lv.Select(x => _n == 10 ? Catalog.GAINS[x + 10] : x).ToArray(), _n == 10 ? 6 : 10);
    }

    void PickPreset()
    {
        if (_preset.SelectedItem is not string name || name == "Custom") { _del.IsEnabled = false; return; }
        int[]? vals = null;
        if (name.StartsWith("★ ")) vals = S.CustomPresets.FirstOrDefault(p => p.Bands == _n && p.Name == name[2..])?.Values;
        else vals = (_n == 10 ? Catalog.EQ_PRESETS_10 : Catalog.EQ_PRESETS_6).FirstOrDefault(p => p.Name == name).V;
        _del.IsEnabled = name.StartsWith("★ ");
        if (vals == null) return;
        Guard(() => { for (int i = 0; i < _n && i < vals.Length; i++) _sl[i].Value = Math.Clamp(vals[i], -10, 10); UpdateLabels(); });
        Send();
    }

    void Send()
    {
        _deb.Stop();
        if (!C.IsConnected) return;
        C.SendEq(Levels());
    }

    async void SavePreset()
    {
        var tb = new TextBox { Text = "My preset", MinWidth = 300 };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Name this EQ setting:", Margin = new Thickness(0, 0, 0, 8) });
        sp.Children.Add(tb);
        var mb = new W.MessageBox { Title = "Save custom preset", Content = sp, PrimaryButtonText = "Save", CloseButtonText = "Cancel" };
        if (Application.Current.MainWindow is { IsVisible: true } o) mb.Owner = o;
        tb.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        if (await mb.ShowDialogAsync(true, default) != W.MessageBoxResult.Primary) return;
        var name = tb.Text.Trim();
        if (name.Length == 0) return;
        S.CustomPresets.RemoveAll(p => p.Bands == _n && p.Name == name);
        S.CustomPresets.Add(new CustomPreset { Name = name, Bands = _n, Values = Levels() });
        AppSettings.Save();
        Guard(() => FillPresets("★ " + name));
    }

    async void DeletePreset()
    {
        if (_preset.SelectedItem is not string name || !name.StartsWith("★ ")) return;
        if (!await Ui.Confirm("Delete preset", $"Delete the custom preset “{name[2..]}”?", "Delete", danger: true)) return;
        S.CustomPresets.RemoveAll(p => p.Bands == _n && p.Name == name[2..]);
        AppSettings.Save();
        Guard(() => FillPresets());
    }

    protected override void OnState(StateUpdate s) { }
    protected override void OnAll() { _bands.IsEnabled = true; }
}
