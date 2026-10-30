using System.Windows;
using System.Windows.Controls;
using EarKit.Core;
using EarKit.Services;
namespace EarKit.Pages;

public abstract class PageBase : UserControl
{
    protected static DeviceController C => DeviceController.I;
    protected static AppSettings S => AppSettings.Current;
    protected readonly StackPanel Root = new() { MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };
    protected bool Sync;

    protected PageBase()
    {
        Content = Root;
        C.ModelChanged += () => Guard(() => { OnModel(); OnAll(); });
        C.ConnectionChanged += () => Guard(OnAll);
        C.StateChanged += s => Guard(() => OnState(s));
        Loaded += (_, _) => Guard(OnAll);
    }

    /// Suppress control events during UI updates to avoid writing changes back to the headset.
    protected void Guard(Action a)
    {
        bool old = Sync; Sync = true;
        try { a(); }
        catch (Exception e) { CrashLog.Write(GetType().Name, e); }
        finally { Sync = old; }
    }

    /// Run only for user actions, not programmatic synchronization.
    protected Action<T> User<T>(Action<T> a) => v => { if (!Sync) a(v); };
    protected Action User(Action a) => () => { if (!Sync) a(); };

    protected virtual void OnModel() { }
    protected virtual void OnState(StateUpdate s) => OnAll();
    protected virtual void OnAll() { }
    protected bool Connected => C.IsConnected && C.Model != null;
}
