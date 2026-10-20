using System.Text.Json;
using Microsoft.Win32;
namespace EarKit.Services;

public sealed class CustomPreset
{
    public string Name { get; set; } = "";
    public int Bands { get; set; } = 10;
    public int[] Values { get; set; } = Array.Empty<int>();
}

public sealed class AppSettings
{
    public string Theme { get; set; } = "system";          // system / light / dark
    public string Backdrop { get; set; } = "mica";         // mica / acrylic / tabbed / none
    public int BackdropOpacity { get; set; } = 75;         // 背景色覆盖不透明度 0–100
    public bool StartMinimized { get; set; } = true;       // 开机自启时最小化到托盘
    public bool CloseToTray { get; set; } = true;
    public bool AutoConnect { get; set; } = true;
    public string? LastMac { get; set; }
    public string? LastName { get; set; }
    public string? LastCom { get; set; }
    public string ManualModel { get; set; } = "";          // 空 = 自动识别
    public bool UseCom { get; set; }                       // 高级：经蓝牙串口 COM 连接
    public string? ComPort { get; set; }
    public int RfcommChannel { get; set; }                 // 0 = 自动
    public bool LowBatteryAlert { get; set; } = true;
    public int LowBatteryThreshold { get; set; } = 20;
    public bool HotkeyEnabled { get; set; } = true;
    public int HotkeyMods { get; set; } = 0x0002 | 0x0001;  // Ctrl + Alt
    public int HotkeyVk { get; set; } = 0x4E;               // N
    public bool TrayHintShown { get; set; }
    public bool UnlockAll { get; set; }
    public bool EqAutoApply { get; set; } = true;
    public bool ShowRawHex { get; set; } = true;
    public bool NotifyConnect { get; set; } = true;
    public Dictionary<string, int[]> LastEq { get; set; } = new();
    public List<CustomPreset> CustomPresets { get; set; } = new();
    public double WinW { get; set; } = 1100;
    public double WinH { get; set; } = 760;

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EarKit");
    static string FilePath => Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        Migrate();
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Opt) ?? new AppSettings();
        }
        catch { Current = new AppSettings(); }
    }

    /// 从旧名称 “Earfun Tools” 迁移设置（%AppData%\EarfunTools → %AppData%\EarKit）
    static void Migrate()
    {
        try
        {
            var old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EarfunTools");
            if (!File.Exists(FilePath) && Directory.Exists(old))
            {
                Directory.CreateDirectory(Dir);
                foreach (var f in Directory.GetFiles(old))
                {
                    var dst = Path.Combine(Dir, Path.GetFileName(f));
                    if (!File.Exists(dst)) File.Copy(f, dst);
                }
            }
        }
        catch { }
        AutoStart.MigrateLegacy();
    }

    static System.Windows.Threading.DispatcherTimer? _t;
    /// 合并 500ms 内的多次保存
    public static void Save()
    {
        if (System.Windows.Application.Current?.Dispatcher is { } d && d.CheckAccess())
        {
            _t ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _t.Stop();
            _t.Tick -= OnTick; _t.Tick += OnTick;
            _t.Start();
        }
        else SaveNow();
    }
    static void OnTick(object? s, EventArgs e) { _t!.Stop(); SaveNow(); }
    public static void SaveNow()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Opt));
            File.Move(tmp, FilePath, true);
        }
        catch { }
    }
}

public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "EarKit";
    const string LegacyName = "Earfun Tools";
    /// 旧版的开机自启项改写为新名称和当前路径
    public static void MigrateLegacy()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k?.GetValue(LegacyName) is string v)
            {
                k.DeleteValue(LegacyName, false);
                if (k.GetValue(Name) == null)
                    k.SetValue(Name, $"\"{Environment.ProcessPath}\"" + (v.Contains("--tray") ? " --tray" : ""));
            }
        }
        catch { }
    }
    public static bool IsEnabled()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(Name) is string; } catch { return false; }
    }
    public static void Set(bool on, bool minimized)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue(Name, $"\"{Environment.ProcessPath}\"" + (minimized ? " --tray" : ""));
            else if (k.GetValue(Name) != null) k.DeleteValue(Name, false);
        }
        catch { }
    }
}

public static class CrashLog
{
    public static string PathFile => System.IO.Path.Combine(AppSettings.Dir, "crash.log");
    public static void Write(string where, Exception? e)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            File.AppendAllText(PathFile, $"==== {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] v{typeof(CrashLog).Assembly.GetName().Version}\r\n{e}\r\n\r\n");
        }
        catch { }
    }
}
