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

    /// 在更新界面时屏蔽控件事件，避免回写耳机
    protected void Guard(Action a)
    {
        bool old = Sync; Sync = true;
        try { a(); }
        catch (Exception e) { CrashLog.Write(GetType().Name, e); }
        finally { Sync = old; }
    }

    /// 只有用户操作（非程序同步）时才执行
    protected Action<T> User<T>(Action<T> a) => v => { if (!Sync) a(v); };
    protected Action User(Action a) => () => { if (!Sync) a(); };

    protected virtual void OnModel() { }
    protected virtual void OnState(StateUpdate s) => OnAll();
    protected virtual void OnAll() { }
    protected bool Connected => C.IsConnected && C.Model != null;
}
