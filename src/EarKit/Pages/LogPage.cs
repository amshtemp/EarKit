using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EarKit.Controls;
using EarKit.Core;
using EarKit.Services;
using Microsoft.Win32;
using W = Wpf.Ui.Controls;
namespace EarKit.Pages;

public sealed class LogPage : UserControl
{
    static DeviceController C => DeviceController.I;
    readonly TextBox _log = new()
    {
        IsReadOnly = true, FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"), FontSize = 12,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        TextWrapping = TextWrapping.NoWrap, AcceptsReturn = true, VerticalContentAlignment = VerticalAlignment.Top,
    };
    readonly TextBox _hex = new() { MinWidth = 280 };
    readonly W.ToggleSwitch _raw = new();
    int _lines;
    readonly StringBuilder _pending = new();
    readonly System.Windows.Threading.DispatcherTimer _flush = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public LogPage()
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(Ui.Header("Debug log", "Raw data and parsed results sent and received. Save and share the log if you encounter a problem."));

        var bar = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        for (int i = 0; i < 6; i++) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 2 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        _hex.ToolTip = "Raw HEX to send, for example: FF 04 00 00 00 0A 03 09";
        bar.Children.Add(_hex);
        var send = Ui.Btn("Send", W.SymbolRegular.Play24, SendHex); send.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(send, 1); bar.Children.Add(send);
        var rp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
        rp.Children.Add(new TextBlock { Text = "Show raw frames", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        _raw.IsChecked = AppSettings.Current.ShowRawHex;
        _raw.Checked += (_, _) => { AppSettings.Current.ShowRawHex = true; AppSettings.Save(); Reload(); };
        _raw.Unchecked += (_, _) => { AppSettings.Current.ShowRawHex = false; AppSettings.Save(); Reload(); };
        rp.Children.Add(_raw);
        Grid.SetColumn(rp, 3); bar.Children.Add(rp);
        var clear = Ui.Btn("Clear", W.SymbolRegular.Delete24, () => { C.LogBuffer.Clear(); Reload(); }); clear.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(clear, 4); bar.Children.Add(clear);
        var save = Ui.Btn("Save log…", W.SymbolRegular.Save24, Save); save.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(save, 5); bar.Children.Add(save);
        Grid.SetRow(bar, 1); g.Children.Add(bar);
        Grid.SetRow(_log, 2); g.Children.Add(_log);
        Content = g;

        _flush.Tick += (_, _) => Flush();
        C.Logged += e => { _pending.Append(Fmt(e)); if (!_flush.IsEnabled) _flush.Start(); };
        Reload();
    }

    static string Fmt(LogEntry e)
    {
        var sb = new StringBuilder();
        sb.Append(e.Time.ToString("HH:mm:ss.fff")).Append(' ').Append(e.Dir).Append(' ');
        if (e.Data is { Length: > 0 } && AppSettings.Current.ShowRawHex)
        {
            sb.Append(Util.Hex(e.Data));
            if (!string.IsNullOrEmpty(e.Text)) sb.Append("   | ");
        }
        sb.Append(e.Text).Append("\r\n");
        return sb.ToString();
    }

    void Flush()
    {
        _flush.Stop();
        if (_pending.Length == 0) return;
        var s = _pending.ToString(); _pending.Clear();
        _lines += s.Count(c => c == '\n');
        if (_lines > 5000) { Reload(); return; }
        bool atEnd = _log.VerticalOffset + _log.ViewportHeight >= _log.ExtentHeight - 4;
        _log.AppendText(s);
        if (atEnd) _log.ScrollToEnd();
    }

    void Reload()
    {
        _pending.Clear();
        var sb = new StringBuilder();
        var list = C.LogBuffer.Skip(Math.Max(0, C.LogBuffer.Count - 3000)).ToList();
        foreach (var e in list) sb.Append(Fmt(e));
        _lines = list.Count;
        _log.Text = sb.ToString();
        _log.ScrollToEnd();
    }

    void SendHex()
    {
        var b = Util.ParseHex(_hex.Text);
        if (b == null || b.Length == 0) { App.MainWin.AddNotice("Invalid HEX format", "Enter hexadecimal bytes separated by spaces, for example: FF 04 00 00 00 0A 03 09", NoticeKind.Warning); return; }
        if (!C.IsConnected) { App.MainWin.AddNotice("Not connected", "Connect a headset first", NoticeKind.Warning); return; }
        C.SendRaw(b);
    }

    void Save()
    {
        var dlg = new SaveFileDialog { FileName = $"earfun_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt", Filter = "Text files|*.txt" };
        if (dlg.ShowDialog() != true) return;
        var sb = new StringBuilder();
        sb.Append($"EarKit {typeof(App).Assembly.GetName().Version} · {Environment.OSVersion} · Model {C.Model?.ToString() ?? "-"} · Firmware {C.Firmware ?? "-"} · {C.LinkDesc ?? "-"}\r\n\r\n");
        bool raw = AppSettings.Current.ShowRawHex;
        AppSettings.Current.ShowRawHex = true;
        foreach (var e in C.LogBuffer) sb.Append(Fmt(e));
        AppSettings.Current.ShowRawHex = raw;
        File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
    }
}
