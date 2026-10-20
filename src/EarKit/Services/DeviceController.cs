using System.Windows.Threading;
using EarKit.Core;
namespace EarKit.Services;

public enum ConnState { Disconnected, Connecting, Connected, Error }
public sealed record LogEntry(DateTime Time, string Dir, byte[]? Data, string Text);
public enum NoticeKind { Info, Success, Warning, Error }

/// 设备控制中心：所有方法与事件均在 UI 线程上运行
public sealed class DeviceController
{
    public static DeviceController I { get; private set; } = null!;
    readonly Dispatcher _d;
    static AppSettings S => AppSettings.Current;

    public Session? Sess { get; private set; }
    public Model? Model { get; private set; }
    public Proto? Proto => Sess?.Proto;
    public bool IsConnected => Sess != null && Sess.Alive;
    public ConnState Conn { get; private set; } = ConnState.Disconnected;
    public string StatusText { get; private set; } = "未连接";
    public string? Firmware { get; private set; }
    public string? LinkDesc { get; private set; }
    public string? ProtocolName => Model == null ? null : Model.IsQcc ? "高通 GAIA" : "EarFun EF";
    public readonly Dictionary<string, object> State = new();
    public readonly Dictionary<string, PhoneInfo> Phones = new();
    public readonly Dictionary<string, int> Plays = new();
    public List<DeviceEntry> Devices { get; private set; } = new();
    public readonly List<LogEntry> LogBuffer = new();

    public event Action<StateUpdate>? StateChanged;
    public event Action? ModelChanged;
    public event Action? ConnectionChanged;
    public event Action? DevicesChanged;
    public event Action? PhonesChanged;
    public event Action<LogEntry>? Logged;
    public event Action<string, string, NoticeKind>? Notice;      // 标题, 内容
    public event Action<string, string>? TrayNotice;              // 仅托盘气泡
    public event Action? PairingChanged;

    public bool UnlockAll => S.UnlockAll;
    public bool Has(string feat) => Model != null && (UnlockAll || Model.Feats.Contains(feat));

    DeviceEntry? _lastEntry; Model? _lastManual; int _lastCh;
    bool _userDisconnected;
    int _autoRetries;
    DispatcherTimer? _reconnectJob, _confirmJob;
    readonly List<DispatcherTimer> _dualJobs = new();
    readonly DispatcherTimer _poll, _watch;
    DateTime _pollHoldUntil;
    readonly Dictionary<string, (object Want, DateTime Until)> _pending = new();
    readonly HashSet<string> _lowAlerted = new();
    bool _wrongFamilyWarned;
    int _quietFails; DateTime _lastQuiet;

    const double HOLD = 6.0, CONFIRM = 2.5;

    public DeviceController(Dispatcher d)
    {
        I = this; _d = d;
        _poll = new DispatcherTimer(DispatcherPriority.Background, d) { Interval = TimeSpan.FromSeconds(5) };
        _poll.Tick += (_, _) => { if (IsConnected && DateTime.Now >= _pollHoldUntil) Query(false); };
        _watch = new DispatcherTimer(DispatcherPriority.Background, d) { Interval = TimeSpan.FromSeconds(20) };
        _watch.Tick += (_, _) => AutoConnectTick();
    }

    // ------------------------------------------------------------ 工具
    DispatcherTimer Later(int ms, Action a)
    {
        var t = new DispatcherTimer(DispatcherPriority.Normal, _d) { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
        t.Tick += (_, _) => { t.Stop(); try { a(); } catch (Exception e) { CrashLog.Write("timer", e); Log("!!", null, "内部错误：" + e.Message); } };
        t.Start();
        return t;
    }
    static void Cancel(ref DispatcherTimer? t) { t?.Stop(); t = null; }

    public void Log(string dir, byte[]? data, string text)
    {
        var e = new LogEntry(DateTime.Now, dir, data, text);
        LogBuffer.Add(e);
        if (LogBuffer.Count > 4000) LogBuffer.RemoveRange(0, 1000);
        Logged?.Invoke(e);
    }
    void LogBg(string dir, byte[]? data, string text) => _d.BeginInvoke(() => Log(dir, data, text));

    void SetStatus(ConnState c, string text)
    {
        Conn = c; StatusText = text;
        ConnectionChanged?.Invoke();
    }
    void Note(string title, string msg, NoticeKind k = NoticeKind.Info) => Notice?.Invoke(title, msg, k);

    // ------------------------------------------------------------ 设备列表
    public async Task RefreshDevicesAsync()
    {
        try
        {
            if (S.UseCom)
            {
                var reg = Discovery.PairedFromRegistry();
                Devices = Discovery.BtComPorts().Select(p => new DeviceEntry
                { Com = p.Port, Mac = p.Mac, Name = p.Mac != null && reg.TryGetValue(p.Mac, out var n) ? n : "" }).ToList();
            }
            else Devices = await Discovery.ListAsync();
            Log("--", null, S.UseCom ? $"发现 {Devices.Count} 个蓝牙 COM 口" : $"发现 {Devices.Count} 个已配对的蓝牙设备");
        }
        catch (Exception e) { Log("!!", null, "枚举设备失败：" + e.Message); }
        DevicesChanged?.Invoke();
    }

    public DeviceEntry? PreferredDevice()
    {
        if (Devices.Count == 0) return null;
        return Devices.FirstOrDefault(e => S.LastMac != null && e.Mac == S.LastMac && (!S.UseCom || e.Com == S.LastCom))
            ?? Devices.FirstOrDefault(e => e.IsEarfun && e.Connected)
            ?? Devices.FirstOrDefault(e => e.IsEarfun) ?? Devices[0];
    }

    // ------------------------------------------------------------ 连接
    public void StartAutoConnect()
    {
        _watch.Start();
        Later(1200, AutoConnectTick);
    }

    async void AutoConnectTick()
    {
        if (!S.AutoConnect || IsConnected || Conn == ConnState.Connecting || _userDisconnected || _reconnectJob != null) return;
        if (S.LastMac == null && S.LastCom == null) return;
        if (_quietFails >= 3 && (DateTime.Now - _lastQuiet).TotalSeconds < 120) return;
        if (!S.UseCom && S.LastMac != null)
        {
            var c = await Discovery.IsConnectedAsync(S.LastMac);
            if (c != true) return;     // 耳机不在线时不尝试，避免打扰
        }
        if (IsConnected || Conn == ConnState.Connecting) return;
        var ent = new DeviceEntry { Mac = S.LastMac, Name = S.LastName ?? "", Com = S.UseCom ? S.LastCom : null };
        _lastQuiet = DateTime.Now;
        Connect(ent, Catalog.ByKey(S.ManualModel), S.RfcommChannel, quiet: true);
    }

    public void Connect(DeviceEntry ent, Model? manual, int channel, bool quiet = false)
    {
        if (IsConnected || Conn == ConnState.Connecting) return;
        if (!quiet) { _userDisconnected = false; _quietFails = 0; }
        _lastEntry = ent; _lastManual = manual; _lastCh = channel;
        var chip = Catalog.UnsupportedChip(ent.Name);
        if (chip != null && !quiet)
            Note("型号可能不受支持", $"“{ent.Name}”在官方 App 中使用 {chip} 方案，协议不同，本工具可能无法控制。仍会尝试连接。", NoticeKind.Warning);
        SetStatus(ConnState.Connecting, "正在连接…");
        _ = DoConnect(ent, manual, channel, quiet);
    }

    async Task DoConnect(DeviceEntry ent, Model? manual, int ch, bool quiet)
    {
        (ILink? link, string? fam, string? ver, string? err) r;
        try { r = await Task.Run(() => ConnectWorker(ent, ch, quiet)); }
        catch (Exception e) { r = (null, null, null, e.Message); }
        if (r.link == null) { OnConnectFailed(r.err ?? "无法建立连接", quiet); return; }
        try { OnConnected(r.link, ent, manual, r.fam, r.ver); }
        catch (Exception e) { CrashLog.Write("OnConnected", e); r.link.Dispose(); OnConnectFailed(e.Message, quiet); }
    }

    (ILink?, string?, string?, string?) ConnectWorker(DeviceEntry ent, int ch, bool quiet)
    {
        LogFn log = LogBg;
        string? lastErr = null;
        (ILink?, string?, string?, string?) Ok(ILink l, string? f, string? v) => (l, f, v, null);

        if (ent.Com != null)
        {
            log("--", null, $"打开串口 {ent.Com}");
            var l = new SerialLink(ent.Com);
            Thread.Sleep(300);
            var (f, v) = Session.ProbeFamily(l, log);
            return Ok(l, f, v);
        }
        if (ent.Mac == null) return (null, null, null, "未选择设备");
        ulong addr = Util.ParseMac(ent.Mac);

        ILink? Try(Func<ILink> mk, string what, out string? fam, out string? ver)
        {
            fam = ver = null;
            ILink? l;
            try { log("--", null, $"尝试 {what} …"); l = mk(); }
            catch (Exception e) { lastErr = e.Message; log("--", null, $"  {what} 失败：{e.Message}"); return null; }
            (fam, ver) = Session.ProbeFamily(l, log);
            if (fam != null) return l;
            log("--", null, $"  {what} 已连接但无协议回应");
            l.Dispose();
            return null;
        }

        string? fm, vr;
        if (ch > 0)
        {
            try
            {
                log("--", null, $"RFCOMM 直连 {ent.Mac} 通道 {ch}");
                var l = new BthSocketLink(addr, ch, 6000);
                var (f, v) = Session.ProbeFamily(l, log);
                return Ok(l, f, v);
            }
            catch (Exception e) { return (null, null, null, $"通道 {ch} 连接失败：{e.Message}"); }
        }
        // 1) Winsock + SPP UUID（由系统查 SDP 得到通道）
        var link = Try(() => new BthSocketLink(addr, 0, 8000), "SPP 串口服务", out fm, out vr);
        if (link != null) return Ok(link, fm, vr);
        // 2) WinRT RFCOMM 服务
        var svcs = Discovery.RfcommServicesAsync(ent.Mac, m => log("--", null, m)).GetAwaiter().GetResult();
        foreach (var s in svcs)
        {
            var desc = $"{ent.Mac} {s.ServiceId.Uuid}";
            link = Try(() => WinRtLink.ConnectAsync(s, desc).GetAwaiter().GetResult(), $"WinRT 服务 {s.ServiceId.Uuid}", out fm, out vr);
            if (link != null) return Ok(link, fm, vr);
        }
        if (quiet) return (null, null, null, lastErr ?? "无协议回应");
        // 3) 逐个 RFCOMM 通道扫描
        for (int c = 1; c <= 30; c++)
        {
            link = Try(() => new BthSocketLink(addr, c, 5000), $"RFCOMM 通道 {c}", out fm, out vr);
            if (link != null) return Ok(link, fm, vr);
        }
        return (null, null, null, "无法建立连接。请确认耳机已在 Windows 中配对并处于连接状态，且未被手机 App 占用。" + (lastErr != null ? $"\n（最后错误：{lastErr}）" : ""));
    }

    void OnConnectFailed(string err, bool quiet)
    {
        Log("!!", null, err);
        if (_autoRetries > 0)
        {
            _autoRetries--;
            SetStatus(ConnState.Connecting, $"耳机可能仍在重启，10 秒后再次尝试重连（剩余 {_autoRetries + 1} 次）");
            ScheduleReconnect(10000);
            return;
        }
        if (quiet) { _quietFails++; SetStatus(ConnState.Disconnected, "未连接"); return; }
        SetStatus(ConnState.Error, "连接失败");
        Note("连接失败", err, NoticeKind.Error);
    }

    void OnConnected(ILink link, DeviceEntry ent, Model? manual, string? fam, string? ver)
    {
        _autoRetries = 0; _quietFails = 0; _wrongFamilyWarned = false;
        Cancel(ref _reconnectJob);
        var model = manual;
        if (fam == null)
            Log("!!", null, "两种协议都没有回应。可能：COM 口不对 / 耳机未连接 / 被手机 App 占用。仍按所选型号继续，请看日志。");
        var auto = Catalog.GuessModel(ver) ?? Catalog.GuessModel(ent.Name);
        if (model == null)
        {
            model = auto != null && (fam == null || auto.Family == fam) ? auto : null;
            if (model == null)
            {
                model = Catalog.ByKey(fam != "ef" ? "TW600" : "TW220")!;
                Log("!!", null, $"未能精确识别型号，按协议族默认使用 {model}，可在“应用设置”中手动指定型号");
                Note("未能精确识别型号", $"已按协议默认使用 {model.Name}。如功能不对，可在“应用设置 → 连接”中手动指定型号。", NoticeKind.Warning);
            }
        }
        else if (fam != null && model.Family != fam)
        {
            var nm = auto != null && auto.Family == fam ? auto : Catalog.ByKey(fam == "qcc" ? "TW600" : "TW220")!;
            Note("型号与协议不符", $"手动指定的 {model.Name} 与耳机实际应答的协议不一致，已自动改用 {nm.Name}。", NoticeKind.Warning);
            model = nm;
        }
        State.Clear(); Phones.Clear(); Plays.Clear(); _pending.Clear(); _lowAlerted.Clear();
        Firmware = string.IsNullOrEmpty(ver) ? null : ver;
        LinkDesc = link.Desc;
        Session? me = null;
        me = new Session(link, model, LogBg, upd => _d.BeginInvoke(() => { if (Sess == me) OnState(upd); }));
        Sess = me;
        SetModelInternal(model);
        S.LastMac = ent.Mac; S.LastName = string.IsNullOrEmpty(ent.Name) ? S.LastName : ent.Name; S.LastCom = ent.Com;
        AppSettings.Save();
        SetStatus(ConnState.Connected, "已连接");
        Log("--", null, $"已连接 {link.Desc} · 型号 {model.Name} [{model.Key}] · 协议 {ProtocolName}" + (Firmware != null ? $" · 固件 {Firmware}" : ""));
        if (S.NotifyConnect) TrayNotice?.Invoke("耳机已连接", model.Name);
        var init = Sess.Proto.InitCmds();
        if (init.Count > 0) Sess.Send(init, "初始化序列");
        Query(true);
        _poll.Start();
    }

    public void Disconnect(bool keepReconnect = false, bool user = false)
    {
        if (user) { _userDisconnected = true; _autoRetries = 0; }
        if (!keepReconnect) Cancel(ref _reconnectJob);
        foreach (var j in _dualJobs) j.Stop();
        _dualJobs.Clear();
        _poll.Stop();
        StopPairing(false, sendStop: IsConnected);
        var s = Sess; Sess = null;
        s?.Dispose();
        _pending.Clear();
        Cancel(ref _confirmJob);
        if (Conn != ConnState.Connecting || !keepReconnect) SetStatus(ConnState.Disconnected, "未连接");
        else ConnectionChanged?.Invoke();
    }

    public void Reconnect()
    {
        var ent = _lastEntry ?? PreferredDevice();
        if (ent == null) { Note("无法重连", "没有可用的设备，请先在 Windows 中配对耳机。", NoticeKind.Warning); return; }
        if (IsConnected) Disconnect();
        Connect(ent, _lastEntry != null ? _lastManual : Catalog.ByKey(S.ManualModel), _lastEntry != null ? _lastCh : S.RfcommChannel);
    }

    void ScheduleReconnect(int ms)
    {
        Cancel(ref _reconnectJob);
        _reconnectJob = Later(ms, () =>
        {
            _reconnectJob = null;
            if (!IsConnected && _lastEntry != null)
            {
                if (Conn == ConnState.Connecting) Conn = ConnState.Disconnected;
                Log("--", null, "自动重连…");
                Connect(_lastEntry, _lastManual, _lastCh, quiet: true);
            }
        });
    }

    // ------------------------------------------------------------ 型号
    void SetModelInternal(Model m)
    {
        Model = m;
        Phones.Clear(); Plays.Clear();
        ModelChanged?.Invoke();
        PhonesChanged?.Invoke();
    }

    public void SetManualModel(Model? m)
    {
        S.ManualModel = m?.Key ?? "";
        AppSettings.Save();
        if (m != null && Sess != null && m != Model)
        {
            Sess.SetModel(m);
            SetModelInternal(m);
            Log("--", null, $"手动切换型号为 {m}");
            Query(true);
        }
    }

    public void SetUnlock(bool on)
    {
        S.UnlockAll = on; AppSettings.Save();
        ModelChanged?.Invoke();
    }

    // ------------------------------------------------------------ 查询 / 下发
    public void Query(bool full)
    {
        if (Sess == null || !Sess.Alive) return;
        Sess.Send(Sess.Proto.Queries(full), full ? "查询" : "");
        if (full && Model != null && Model.Feats.Contains("dual") && Model.Dual.List) RefreshDevList();
    }

    public bool Do(List<byte[]> frames, string note = "", StateUpdate? expect = null)
    {
        if (Sess == null || !Sess.Alive) { Note("未连接", "请先连接耳机", NoticeKind.Warning); return false; }
        if (frames.Count == 0) { Log("..", null, $"{note}：该型号协议不支持此操作"); return false; }
        Sess.Send(frames, note);
        if (expect != null && expect.Count > 0)
        {
            var until = DateTime.Now.AddSeconds(HOLD);
            foreach (var kv in expect) { _pending[kv.Key] = (kv.Value, until); State[kv.Key] = kv.Value; }
            StateChanged?.Invoke(expect);
            Cancel(ref _confirmJob);
            _confirmJob = Later((int)(CONFIRM * 1000), Confirm);
            _pollHoldUntil = DateTime.Now.AddSeconds(HOLD + 0.5);
        }
        return true;
    }

    void Confirm()
    {
        _confirmJob = null;
        var now = DateTime.Now;
        foreach (var k in _pending.Where(p => p.Value.Until <= now).Select(p => p.Key).ToList()) _pending.Remove(k);
        Query(true);
        if (_pending.Count > 0) Later((int)((HOLD - CONFIRM) * 1000) + 300, () => Query(true));
    }

    StateUpdate FilterPending(StateUpdate s)
    {
        if (_pending.Count == 0) return s;
        var now = DateTime.Now; var o = new StateUpdate();
        foreach (var kv in s)
        {
            if (!_pending.TryGetValue(kv.Key, out var p)) { o[kv.Key] = kv.Value; continue; }
            if (Equals(kv.Value, p.Want)) { _pending.Remove(kv.Key); o[kv.Key] = kv.Value; }
            else if (now < p.Until) Log("..", null, $"{kv.Key} 读到 {kv.Value}，耳机仍在切换到 {p.Want}，暂不刷新界面");
            else { _pending.Remove(kv.Key); o[kv.Key] = kv.Value; Log("!!", null, $"{kv.Key} 设置为 {p.Want} 后耳机仍报告 {kv.Value}，可能未生效"); }
        }
        return o;
    }

    void OnState(StateUpdate s)
    {
        if (s.TryGetValue("_disconnected", out var why))
        {
            if (Sess == null) return;
            Log("!!", null, "连接已断开：" + why);
            bool rebooting = _reconnectJob != null;
            Disconnect(keepReconnect: true);
            if (!rebooting && S.AutoConnect && !_userDisconnected && _lastEntry != null)
            {
                _autoRetries = Math.Max(_autoRetries, 2);
                ScheduleReconnect(5000);
                SetStatus(ConnState.Connecting, "连接已断开，稍后自动重连…");
            }
            TrayNotice?.Invoke("耳机已断开", why?.ToString() ?? "");
            return;
        }
        if (s.ContainsKey("_wrong_family"))
        {
            if (!_wrongFamilyWarned)
            {
                _wrongFamilyWarned = true;
                Note("协议不符", $"耳机回应的是{((string)s["_wrong_family"] == "qcc" ? "高通" : "EarFun")}协议帧，型号可能选错了。可在“应用设置”中改为自动识别或手动指定型号。", NoticeKind.Warning);
            }
            return;
        }
        s = FilterPending(s);
        if (s.Count == 0) return;
        foreach (var kv in s) State[kv.Key] = kv.Value;
        if (s.TryGetValue("version", out var v) && v is string vs && vs.Length > 0) Firmware = vs;
        bool phones = false;
        if (s.TryGetValue("phone", out var po) && po is PhoneInfo p)
        {
            if (Model?.Variant == "tw600" && p.Num == 0) Phones.Clear();   // TW600：number==0 时清空列表
            Phones[p.Mac] = p; phones = true;
        }
        if (s.TryGetValue("play", out var pl) && pl is PlayInfo pi) { Plays[pi.Mac] = pi.Play; phones = true; }
        CheckBattery(s);
        StateChanged?.Invoke(s);
        if (phones) PhonesChanged?.Invoke();
    }

    void CheckBattery(StateUpdate s)
    {
        if (!S.LowBatteryAlert || Model == null) return;
        foreach (var (k, label) in new[] { ("bat_l", Model.IsHead ? "耳机" : "左耳"), ("bat_r", "右耳") })
        {
            if (!s.TryGetValue(k, out var o) || o is not int v || v < 0 || v > 100) continue;
            bool charging = State.TryGetValue(k == "bat_l" ? "chg_l" : "chg_r", out var c) && c is true;
            if (v <= S.LowBatteryThreshold && !charging && _lowAlerted.Add(k))
                TrayNotice?.Invoke("耳机电量低", $"{Model.Name} {label}电量 {v}%，请及时充电");
            else if (v > S.LowBatteryThreshold + 5) _lowAlerted.Remove(k);
        }
    }

    public T? Get<T>(string k) => State.TryGetValue(k, out var v) && v is T t ? t : default;
    public bool TryGet<T>(string k, out T val)
    {
        if (State.TryGetValue(k, out var v) && v is T t) { val = t; return true; }
        val = default!; return false;
    }

    // ------------------------------------------------------------ 常用操作
    public void SetNoise(string mode) { if (Proto != null) Do(Proto.SetNoise(mode), $"降噪 → {Catalog.NOISE_LABEL.GetValueOrDefault(mode, mode)}", new() { ["noise"] = mode }); }
    public void SetSub(string sub) { if (Proto != null) Do(Proto.SetSub(sub), $"降噪子模式 → {Catalog.SUB_LABEL.GetValueOrDefault(sub, sub)}", new() { ["sub"] = sub }); }
    public void SetAmb(string a) { if (Proto != null) Do(Proto.SetAmb(a), $"通透子模式 → {Catalog.AMB_LABEL.GetValueOrDefault(a, a)}", new() { ["amb"] = a }); }
    public void SetGame(bool on) { if (Proto != null) Do(Proto.SetGame(on), "游戏模式 → " + (on ? "开" : "关"), new() { ["game"] = on }); }
    public void SetSpatial(bool on) { if (Proto != null) Do(Proto.SetSpatial(on), "空间音频 → " + (on ? "开" : "关"), new() { ["spatial"] = on }); }
    public void SetEar(bool on) { if (Proto != null) Do(Proto.SetEar(on), "入耳检测 → " + (on ? "开" : "关"), new() { ["ear"] = on }); }
    public void SetPrompt(int v) { if (Proto != null) Do(Proto.SetPrompt(v), $"提示音音量 → {v}%", new() { ["prompt"] = v }); }
    public void SetAutoOff(int m) { if (Proto != null) Do(Proto.SetAutoOff(m), $"自动关机 → {Catalog.AutoOffLabel(m)}", new() { ["autooff"] = m }); }
    public void SetMic(string w) { if (Proto != null) Do(Proto.SetMic(w), $"通话麦克风 → {w}", new() { ["mic"] = w }); }
    public void SetFind(bool l, bool r) { if (Proto != null) Do(Proto.SetFind(l, r), "查找耳机", new() { ["find_l"] = l, ["find_r"] = r }); }
    public void SetDisableKeys(bool l, bool r) { if (Proto != null) Do(Proto.SetDisableKeys(l, r), "禁用触控", new() { ["dis_l"] = l, ["dis_r"] = r }); }
    public void SetQuality(bool ldac, bool aptx, bool adaptive, bool lossless)
    {
        if (Proto != null) Do(Proto.SetQuality(ldac, aptx, adaptive, lossless), "高清音频编码",
            new() { ["ldac"] = ldac, ["aptx"] = aptx, ["adaptive"] = adaptive, ["lossless"] = lossless });
    }
    public void ResetFactory() { if (Proto != null) Do(Proto.Reset(), "恢复出厂"); }
    public void ReadKeys() { if (Proto != null) Do(Proto.KeyQuery(), "读取按键"); }

    public void SetKey(string side, string gesture, string op)
    {
        if (Proto == null) return;
        var cur = Get<Dictionary<string, string>>("keys") is { } k ? new Dictionary<string, string>(k) : new Dictionary<string, string>();
        if (Do(Proto.SetKey(side, gesture, op, cur), $"按键 {side}-{gesture} → {Catalog.OpLabel(op)}"))
        {
            cur[EarKit.Core.Proto.KK(side, gesture)] = op;
            State["keys"] = cur;
        }
    }

    public void SendEq(int[] levels)
    {
        if (Sess == null || Model == null) return;
        var frames = Sess.Proto.Eq(levels);
        var note = $"EQ [{string.Join(", ", levels)}]";
        if (Model.CombineEq) Sess.SendCombined(frames, note); else Sess.Send(frames, note);
        S.LastEq[Model.Key] = levels.ToArray();
        AppSettings.Save();
    }

    public void SendRaw(byte[] b) { if (Sess != null && Sess.Alive) Sess.Send(new[] { b }, "手动发送"); }

    /// 托盘/快捷键：按型号支持的模式循环切换
    public string? CycleNoise()
    {
        if (!IsConnected || Model == null || Model.Noise.Count == 0) return null;
        var list = Model.Noise;
        var cur = Get<string>("noise");
        int i = cur != null ? list.IndexOf(cur) : -1;
        var next = list[(i + 1) % list.Count];
        SetNoise(next);
        return Catalog.NOISE_LABEL.GetValueOrDefault(next, next);
    }

    // ------------------------------------------------------------ 多设备连接
    public DualCfg Dual => Model?.Dual ?? new DualCfg();
    public List<PhoneInfo> ConnectedPhones() => Phones.Values.Where(p => p.Connected).ToList();
    public static bool IsThisPc(PhoneInfo p)
    {
        var host = Environment.MachineName.ToLowerInvariant();
        return host.Length > 0 && p.Name.ToLowerInvariant().Contains(host);
    }

    public void RefreshDevList()
    {
        if (!IsConnected || Model == null || !Has("dual")) return;
        Phones.Clear(); Plays.Clear(); PhonesChanged?.Invoke();
        Sess!.Send(Sess.Proto.DevList(), "读取多设备连接列表");
    }
    void DualLater(params int[] delays) { foreach (var d in delays) _dualJobs.Add(Later(d, RefreshDevList)); }

    public bool LdacConflict(bool on) => on && Dual.Ldac && Get<bool>("ldac");

    public void SetDual(bool on, bool turnOffLdac)
    {
        if (Proto == null) return;
        var cfg = Dual;
        if (turnOffLdac)
            Do(Proto.SetQuality(false, Get<bool>("aptx"), Get<bool>("adaptive"), Get<bool>("lossless")), "关闭 LDAC（多设备连接需要）", new() { ["ldac"] = false });
        Do(Proto.SetDual(on), $"多设备连接 → {(on ? "开" : "关")}", new() { ["dual"] = on });
        if ((on && cfg.OpenReset) || cfg.ToggleReset)
        {
            Log("--", null, "该型号切换多设备连接后会自动重启，约 20 秒后自动重连");
            SetStatus(ConnState.Connected, "耳机正在重启以应用多设备设置，约 20 秒后自动重连…");
            Note("耳机即将重启", "该型号切换多设备连接后会自动重启，约 20 秒后自动重连。", NoticeKind.Info);
            Later(1500, () => { if (Sess != null) Disconnect(keepReconnect: true); SetStatus(ConnState.Connecting, "等待耳机重启…"); });
            _autoRetries = 4;
            ScheduleReconnect(20000);
        }
        else if (on) DualLater(2000, 10000);
    }

    void SendDevCmd(List<byte[]> frames, string note)
    {
        foreach (var j in _dualJobs) j.Stop();
        _dualJobs.Clear();
        var snap = string.Join(",", ConnectedPhones().Select(p => p.Mac).OrderBy(x => x));
        Sess!.Send(frames, note);
        if (Dual.Resend)
        {
            void Again(int i)
            {
                var cur = string.Join(",", ConnectedPhones().Select(p => p.Mac).OrderBy(x => x));
                if (IsConnected && cur == snap) Sess!.Send(frames, $"{note}（重发 {i}/2）");
            }
            _dualJobs.Add(Later(3000, () => Again(1)));
            _dualJobs.Add(Later(6000, () => Again(2)));
        }
        DualLater(2000, 5000, 8000, 11000);
    }

    public void DevConnect(PhoneInfo p)
    {
        if (!IsConnected) return;
        if (ConnectedPhones().Count >= 2) { Note("已满", "耳机已连接两台设备，请先断开其中一台", NoticeKind.Warning); return; }
        SendDevCmd(Proto!.DevConnect(p.Num), $"连接设备 {p.Name}");
    }
    public void DevDisconnect(PhoneInfo p)
    {
        if (!IsConnected) return;
        SendDevCmd(Proto!.DevDisconnect(p.Num), $"断开设备 {p.Name}");
    }
    public void DevPlay(PhoneInfo p, int state)
    {
        if (!IsConnected) return;
        var fr = Proto!.SetPlay(p.Mac, state);
        if (fr.Count > 0) { Sess!.Send(fr, $"{(state == 1 ? "设为播放" : "挂起")} {p.Name}"); DualLater(1500); }
    }

    // 配对新设备
    public bool Pairing { get; private set; }
    public int PairingLeft { get; private set; }
    public bool? PairingResult { get; private set; }
    int _pairBase; DateTime _pairStart; DispatcherTimer? _pairTick;

    public void StartPairing()
    {
        if (!IsConnected || Pairing) return;
        if (ConnectedPhones().Count >= 2) { Note("已满", "耳机已连接两台设备，请先断开其中一台", NoticeKind.Warning); return; }
        Sess!.Send(Proto!.NewConnect(), "进入配对模式（连接新设备）");
        Pairing = true; PairingResult = null; _pairBase = ConnectedPhones().Count; _pairStart = DateTime.Now; PairingLeft = 120;
        _pairTick?.Stop();
        _pairTick = new DispatcherTimer(DispatcherPriority.Normal, _d) { Interval = TimeSpan.FromSeconds(1) };
        _pairTick.Tick += (_, _) =>
        {
            if (!Pairing) { _pairTick?.Stop(); return; }
            if (ConnectedPhones().Count > _pairBase) { StopPairing(true); return; }
            PairingLeft = 120 - (int)(DateTime.Now - _pairStart).TotalSeconds;
            if (PairingLeft <= 0 || !IsConnected) { StopPairing(false); return; }
            if (PairingLeft % 3 == 0) RefreshDevList();
            PairingChanged?.Invoke();
        };
        _pairTick.Start();
        PairingChanged?.Invoke();
    }

    public void StopPairing(bool ok, bool sendStop = true)
    {
        if (!Pairing) return;
        Pairing = false; PairingResult = ok;
        _pairTick?.Stop(); _pairTick = null;
        if (IsConnected && sendStop)
        {
            var fr = Proto!.StopNewConnect();
            if (fr.Count > 0 && !ok) Sess!.Send(fr, "退出配对模式");
            RefreshDevList();
        }
        if (ok) Note("新设备已连接", "耳机已连接到新设备。", NoticeKind.Success);
        PairingChanged?.Invoke();
    }
}
