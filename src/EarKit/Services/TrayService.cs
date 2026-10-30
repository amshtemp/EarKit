using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using EarKit.Core;
namespace EarKit.Services;

public sealed class TrayService : IDisposable
{
    readonly TaskbarIcon _tb;
    readonly Action _show, _exit;
    static DeviceController C => DeviceController.I;

    public TrayService(Action show, Action exit)
    {
        _show = show; _exit = exit;
        System.Drawing.Icon icon;
        using (var st = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico")).Stream)
            icon = new System.Drawing.Icon(st);
        _tb = new TaskbarIcon
        {
            Icon = icon,
            ToolTipText = "EarKit",
            NoLeftClickDelay = true,
            MenuActivation = PopupActivationMode.RightClick,
            ContextMenu = new ContextMenu(),
        };
        _tb.TrayLeftMouseUp += (_, _) => _show();
        _tb.TrayMouseDoubleClick += (_, _) => _show();
        _tb.PreviewTrayContextMenuOpen += (_, _) => BuildMenu();
        BuildMenu();
        _tb.ForceCreate(false);
        C.StateChanged += _ => UpdateTip();
        C.ModelChanged += UpdateTip;
        C.ConnectionChanged += UpdateTip;
        C.TrayNotice += (t, m) => Show(t, m);
        UpdateTip();
    }

    public void Show(string title, string msg, bool warn = false)
    {
        try { _tb.ShowNotification(title, string.IsNullOrEmpty(msg) ? " " : msg, warn ? NotificationIcon.Warning : NotificationIcon.Info); }
        catch { }
    }

    static string Bat(string k)
    {
        return C.TryGet<int>(k, out var x) && x >= 0 && x <= 100 ? $"{x}%" : "--";
    }

    void UpdateTip()
    {
        string tip;
        var m = C.Model;
        if (!C.IsConnected || m == null) tip = "EarKit · " + C.StatusText;
        else
        {
            var bat = m.IsHead ? $"Battery {Bat("bat_l")}" : $"L {Bat("bat_l")} · R {Bat("bat_r")} · Case {Bat("bat_box")}";
            var n = C.Get<string>("noise");
            tip = $"{m.Name}\n{bat}" + (m.Noise.Count > 0 && n != null ? $"\nMode: {Catalog.NOISE_LABEL.GetValueOrDefault(n, n)}" : "");
        }
        if (tip.Length > 120) tip = tip[..120];
        try { _tb.ToolTipText = tip; } catch { }
    }

    MenuItem Item(string header, Action a, bool bold = false, bool isChecked = false, bool enabled = true)
    {
        var mi = new MenuItem { Header = header, IsChecked = isChecked, IsEnabled = enabled };
        if (bold) mi.FontWeight = FontWeights.SemiBold;
        mi.Click += (_, _) => { try { a(); } catch (Exception e) { CrashLog.Write("tray", e); } };
        return mi;
    }

    void BuildMenu()
    {
        var cm = _tb.ContextMenu ?? new ContextMenu();
        cm.Items.Clear();
        cm.Items.Add(Item("Open EarKit", _show, bold: true));
        var m = C.Model;
        bool on = C.IsConnected && m != null;
        if (on && m!.Noise.Count > 0)
        {
            cm.Items.Add(new Separator());
            var cur = C.Get<string>("noise");
            foreach (var n in m.Noise)
            {
                var k = n;
                cm.Items.Add(Item(Catalog.NOISE_LABEL.GetValueOrDefault(k, k), () => C.SetNoise(k), isChecked: cur == k));
            }
        }
        if (on && C.Has("game"))
        {
            cm.Items.Add(new Separator());
            var g = C.Get<bool>("game");
            cm.Items.Add(Item("Game mode", () => C.SetGame(!g), isChecked: g));
        }
        cm.Items.Add(new Separator());
        if (on) cm.Items.Add(Item("Disconnect", () => C.Disconnect(user: true)));
        cm.Items.Add(Item(on ? "Reconnect" : "Connect headset", () => C.Reconnect(), enabled: C.Conn != ConnState.Connecting));
        cm.Items.Add(new Separator());
        cm.Items.Add(Item("Exit", _exit));
        _tb.ContextMenu = cm;
    }

    public void Dispose() { try { _tb.Dispose(); } catch { } }
}
