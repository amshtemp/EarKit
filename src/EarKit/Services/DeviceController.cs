using System.Windows.Threading;
using EarKit.Core;
namespace EarKit.Services;

public enum ConnState { Disconnected, Connecting, Connected, Error }
public sealed record LogEntry(DateTime Time, string Dir, byte[]? Data, string Text);
public enum NoticeKind { Info, Success, Warning, Error }

/// Central device controller; all methods and events run on the UI thread.
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
    public string StatusText { get; private set; } = "Not connected";
    public string? Firmware { get; private set; }
    public string? LinkDesc { get; private set; }
    public string? ProtocolName => Model == null ? null : Model.IsQcc ? "Qualcomm GAIA" : "EarFun EF";
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
    public event Action<string, string, NoticeKind>? Notice;      // Title and message
    public event Action<string, string>? TrayNotice;              // Tray notification only
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

    // ------------------------------------------------------------ Utilities
    DispatcherTimer Later(int ms, Action a)
    {
        var t = new DispatcherTimer(DispatcherPriority.Normal, _d) { Interval = TimeSpan.FromMilliseconds(Math.Max(1, ms)) };
        t.Tick += (_, _) => { t.Stop(); try { a(); } catch (Exception e) { CrashLog.Write("timer", e); Log("!!", null, "Internal error: " + e.Message); } };
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

    // ------------------------------------------------------------ Device list
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
            Log("--", null, S.UseCom ? $"Found {Devices.Count} Bluetooth COM port(s)" : $"Found {Devices.Count} paired Bluetooth device(s)");
        }
        catch (Exception e) { Log("!!", null, "Failed to enumerate devices: " + e.Message); }
        DevicesChanged?.Invoke();
    }

    public DeviceEntry? PreferredDevice()
    {
        if (Devices.Count == 0) return null;
        return Devices.FirstOrDefault(e => S.LastMac != null && e.Mac == S.LastMac && (!S.UseCom || e.Com == S.LastCom))
            ?? Devices.FirstOrDefault(e => e.IsEarfun && e.Connected)
            ?? Devices.FirstOrDefault(e => e.IsEarfun) ?? Devices[0];
    }

    // ------------------------------------------------------------ Connection
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
            if (c != true) return;     // Avoid attempting to connect when the headset is offline.
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
            Note("Model may not be supported", $"“{ent.Name}” uses the {chip} platform in the official app, which uses a different protocol. EarKit may not be able to control it. A connection will still be attempted.", NoticeKind.Warning);
        SetStatus(ConnState.Connecting, "Connecting…");
        _ = DoConnect(ent, manual, channel, quiet);
    }

    async Task DoConnect(DeviceEntry ent, Model? manual, int ch, bool quiet)
    {
        (ILink? link, string? fam, string? ver, string? err) r;
        try { r = await Task.Run(() => ConnectWorker(ent, ch, quiet)); }
        catch (Exception e) { r = (null, null, null, e.Message); }
        if (r.link == null) { OnConnectFailed(r.err ?? "Could not establish a connection", quiet); return; }
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
            log("--", null, $"Opening serial port {ent.Com}");
            var l = new SerialLink(ent.Com);
            Thread.Sleep(300);
            var (f, v) = Session.ProbeFamily(l, log);
            return Ok(l, f, v);
        }
        if (ent.Mac == null) return (null, null, null, "No device selected");
        ulong addr = Util.ParseMac(ent.Mac);

        ILink? Try(Func<ILink> mk, string what, out string? fam, out string? ver)
        {
            fam = ver = null;
            ILink? l;
            try { log("--", null, $"Trying {what}…"); l = mk(); }
            catch (Exception e) { lastErr = e.Message; log("--", null, $"  {what} failed: {e.Message}"); return null; }
            (fam, ver) = Session.ProbeFamily(l, log);
            if (fam != null) return l;
            log("--", null, $"  {what} connected, but no protocol response was received");
            l.Dispose();
            return null;
        }

        string? fm, vr;
        if (ch > 0)
        {
            try
            {
                log("--", null, $"Direct RFCOMM connection to {ent.Mac}, channel {ch}");
                var l = new BthSocketLink(addr, ch, 6000);
                var (f, v) = Session.ProbeFamily(l, log);
                return Ok(l, f, v);
            }
            catch (Exception e) { return (null, null, null, $"Connection failed on channel {ch}: {e.Message}"); }
        }
        // 1) Winsock + SPP UUID (the system resolves the channel through SDP).
        var link = Try(() => new BthSocketLink(addr, 0, 8000), "SPP serial service", out fm, out vr);
        if (link != null) return Ok(link, fm, vr);
        // 2) WinRT RFCOMM services.
        var svcs = Discovery.RfcommServicesAsync(ent.Mac, m => log("--", null, m)).GetAwaiter().GetResult();
        foreach (var s in svcs)
        {
            var desc = $"{ent.Mac} {s.ServiceId.Uuid}";
            link = Try(() => WinRtLink.ConnectAsync(s, desc).GetAwaiter().GetResult(), $"WinRT service {s.ServiceId.Uuid}", out fm, out vr);
            if (link != null) return Ok(link, fm, vr);
        }
        if (quiet) return (null, null, null, lastErr ?? "No protocol response");
        // 3) Scan RFCOMM channels one by one.
        for (int c = 1; c <= 30; c++)
        {
            link = Try(() => new BthSocketLink(addr, c, 5000), $"RFCOMM channel {c}", out fm, out vr);
            if (link != null) return Ok(link, fm, vr);
        }
        return (null, null, null, "Could not connect. Make sure the headset is paired in Windows, is connected, and is not in use by the phone app." + (lastErr != null ? $"\n(Last error: {lastErr})" : ""));
    }

    void OnConnectFailed(string err, bool quiet)
    {
        Log("!!", null, err);
        if (_autoRetries > 0)
        {
            _autoRetries--;
            SetStatus(ConnState.Connecting, $"The headset may still be restarting. Retrying in 10 seconds ({_autoRetries + 1} attempt(s) remaining)");
            ScheduleReconnect(10000);
            return;
        }
        if (quiet) { _quietFails++; SetStatus(ConnState.Disconnected, "Not connected"); return; }
        SetStatus(ConnState.Error, "Connection failed");
        Note("Connection failed", err, NoticeKind.Error);
    }

    void OnConnected(ILink link, DeviceEntry ent, Model? manual, string? fam, string? ver)
    {
        _autoRetries = 0; _quietFails = 0; _wrongFamilyWarned = false;
        Cancel(ref _reconnectJob);
        var model = manual;
        if (fam == null)
            Log("!!", null, "Neither protocol responded. Check that the COM port is correct, the headset is connected, and the phone app is not using it. Continuing with the selected model; see the log.");
        var auto = Catalog.GuessModel(ver) ?? Catalog.GuessModel(ent.Name);
        if (model == null)
        {
            model = auto != null && (fam == null || auto.Family == fam) ? auto : null;
            if (model == null)
            {
                model = Catalog.ByKey(fam != "ef" ? "TW600" : "TW220")!;
                Log("!!", null, $"Could not identify the exact model. Using the default for this protocol family: {model}. You can select a model manually in Application Settings.");
                Note("Could not identify exact model", $"Using the protocol's default model, {model.Name}. If features do not work correctly, choose a model manually under Application Settings > Connection.", NoticeKind.Warning);
            }
        }
        else if (fam != null && model.Family != fam)
        {
            var nm = auto != null && auto.Family == fam ? auto : Catalog.ByKey(fam == "qcc" ? "TW600" : "TW220")!;
            Note("Model and protocol do not match", $"The manually selected model, {model.Name}, does not match the protocol reported by the headset. Switched to {nm.Name} automatically.", NoticeKind.Warning);
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
        SetStatus(ConnState.Connected, "Connected");
        Log("--", null, $"Connected {link.Desc} · Model {model.Name} [{model.Key}] · Protocol {ProtocolName}" + (Firmware != null ? $" · Firmware {Firmware}" : ""));
        if (S.NotifyConnect) TrayNotice?.Invoke("Headset connected", model.Name);
        var init = Sess.Proto.InitCmds();
        if (init.Count > 0) Sess.Send(init, "Initialization sequence");
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
        if (Conn != ConnState.Connecting || !keepReconnect) SetStatus(ConnState.Disconnected, "Not connected");
        else ConnectionChanged?.Invoke();
    }

    public void Reconnect()
    {
        var ent = _lastEntry ?? PreferredDevice();
        if (ent == null) { Note("Cannot reconnect", "No device is available. Pair the headset in Windows first.", NoticeKind.Warning); return; }
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
                Log("--", null, "Automatic reconnect…");
                Connect(_lastEntry, _lastManual, _lastCh, quiet: true);
            }
        });
    }

    // ------------------------------------------------------------ Model
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
            Log("--", null, $"Manually switched model to {m}");
            Query(true);
        }
    }

    public void SetUnlock(bool on)
    {
        S.UnlockAll = on; AppSettings.Save();
        ModelChanged?.Invoke();
    }

    // ------------------------------------------------------------ Query / send
    public void Query(bool full)
    {
        if (Sess == null || !Sess.Alive) return;
        Sess.Send(Sess.Proto.Queries(full), full ? "Query" : "");
        if (full && Model != null && Model.Feats.Contains("dual") && Model.Dual.List) RefreshDevList();
    }

    public bool Do(List<byte[]> frames, string note = "", StateUpdate? expect = null)
    {
        if (Sess == null || !Sess.Alive) { Note("Not connected", "Connect a headset first", NoticeKind.Warning); return false; }
        if (frames.Count == 0) { Log("..", null, $"{note}: This operation is not supported by this model's protocol"); return false; }
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
            else if (now < p.Until) Log("..", null, $"Read {kv.Key} as {kv.Value}; headset is still switching to {p.Want}, so the UI will not be refreshed yet");
            else { _pending.Remove(kv.Key); o[kv.Key] = kv.Value; Log("!!", null, $"Headset still reports {kv.Value} after setting {kv.Key} to {p.Want}; the change may not have taken effect"); }
        }
        return o;
    }

    void OnState(StateUpdate s)
    {
        if (s.TryGetValue("_disconnected", out var why))
        {
            if (Sess == null) return;
            Log("!!", null, "Connection lost: " + why);
            bool rebooting = _reconnectJob != null;
            Disconnect(keepReconnect: true);
            if (!rebooting && S.AutoConnect && !_userDisconnected && _lastEntry != null)
            {
                _autoRetries = Math.Max(_autoRetries, 2);
                ScheduleReconnect(5000);
                SetStatus(ConnState.Connecting, "Connection lost; reconnecting shortly…");
            }
            TrayNotice?.Invoke("Headset disconnected", why?.ToString() ?? "");
            return;
        }
        if (s.ContainsKey("_wrong_family"))
        {
            if (!_wrongFamilyWarned)
            {
                _wrongFamilyWarned = true;
                Note("Protocol mismatch", $"The headset responded with a {((string)s["_wrong_family"] == "qcc" ? "Qualcomm" : "EarFun")} protocol frame, so the selected model may be incorrect. Choose automatic detection or select a model manually in Application Settings.", NoticeKind.Warning);
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
            if (Model?.Variant == "tw600" && p.Num == 0) Phones.Clear();   // TW600: number == 0 clears the list.
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
        foreach (var (k, label) in new[] { ("bat_l", Model.IsHead ? "Headset" : "Left earbud"), ("bat_r", "Right earbud") })
        {
            if (!s.TryGetValue(k, out var o) || o is not int v || v < 0 || v > 100) continue;
            bool charging = State.TryGetValue(k == "bat_l" ? "chg_l" : "chg_r", out var c) && c is true;
            if (v <= S.LowBatteryThreshold && !charging && _lowAlerted.Add(k))
                TrayNotice?.Invoke("Low headset battery", $"{Model.Name} {label} battery is at {v}%. Please charge soon.");
            else if (v > S.LowBatteryThreshold + 5) _lowAlerted.Remove(k);
        }
    }

    public T? Get<T>(string k) => State.TryGetValue(k, out var v) && v is T t ? t : default;
    public bool TryGet<T>(string k, out T val)
    {
        if (State.TryGetValue(k, out var v) && v is T t) { val = t; return true; }
        val = default!; return false;
    }

    // ------------------------------------------------------------ Common operations
    public void SetNoise(string mode) { if (Proto != null) Do(Proto.SetNoise(mode), $"Noise control -> {Catalog.NOISE_LABEL.GetValueOrDefault(mode, mode)}", new() { ["noise"] = mode }); }
    public void SetSub(string sub) { if (Proto != null) Do(Proto.SetSub(sub), $"Noise control submode -> {Catalog.SUB_LABEL.GetValueOrDefault(sub, sub)}", new() { ["sub"] = sub }); }
    public void SetAmb(string a) { if (Proto != null) Do(Proto.SetAmb(a), $"Ambient submode -> {Catalog.AMB_LABEL.GetValueOrDefault(a, a)}", new() { ["amb"] = a }); }
    public void SetGame(bool on) { if (Proto != null) Do(Proto.SetGame(on), "Game mode -> " + (on ? "On" : "Off"), new() { ["game"] = on }); }
    public void SetSpatial(bool on) { if (Proto != null) Do(Proto.SetSpatial(on), "Spatial audio -> " + (on ? "On" : "Off"), new() { ["spatial"] = on }); }
    public void SetEar(bool on) { if (Proto != null) Do(Proto.SetEar(on), "In-ear detection -> " + (on ? "On" : "Off"), new() { ["ear"] = on }); }
    public void SetPrompt(int v) { if (Proto != null) Do(Proto.SetPrompt(v), $"Prompt volume -> {v}%", new() { ["prompt"] = v }); }
    public void SetAutoOff(int m) { if (Proto != null) Do(Proto.SetAutoOff(m), $"Auto power-off -> {Catalog.AutoOffLabel(m)}", new() { ["autooff"] = m }); }
    public void SetMic(string w) { if (Proto != null) Do(Proto.SetMic(w), $"Call microphone -> {w}", new() { ["mic"] = w }); }
    public void SetFind(bool l, bool r) { if (Proto != null) Do(Proto.SetFind(l, r), "Find headset", new() { ["find_l"] = l, ["find_r"] = r }); }
    public void SetDisableKeys(bool l, bool r) { if (Proto != null) Do(Proto.SetDisableKeys(l, r), "Disable touch controls", new() { ["dis_l"] = l, ["dis_r"] = r }); }
    public void SetQuality(bool ldac, bool aptx, bool adaptive, bool lossless)
    {
        if (Proto != null) Do(Proto.SetQuality(ldac, aptx, adaptive, lossless), "High-quality audio codecs",
            new() { ["ldac"] = ldac, ["aptx"] = aptx, ["adaptive"] = adaptive, ["lossless"] = lossless });
    }
    public void ResetFactory() { if (Proto != null) Do(Proto.Reset(), "Factory reset"); }
    public void ReadKeys() { if (Proto != null) Do(Proto.KeyQuery(), "Read controls"); }

    public void SetKey(string side, string gesture, string op)
    {
        if (Proto == null) return;
        var cur = Get<Dictionary<string, string>>("keys") is { } k ? new Dictionary<string, string>(k) : new Dictionary<string, string>();
        if (Do(Proto.SetKey(side, gesture, op, cur), $"Control {side}-{gesture} -> {Catalog.OpLabel(op)}"))
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

    public void SendRaw(byte[] b) { if (Sess != null && Sess.Alive) Sess.Send(new[] { b }, "Manual send"); }

    /// Cycle through the modes supported by the model from the tray or hotkey.
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

    // ------------------------------------------------------------ Multipoint
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
        Sess!.Send(Sess.Proto.DevList(), "Read multipoint device list");
    }
    void DualLater(params int[] delays) { foreach (var d in delays) _dualJobs.Add(Later(d, RefreshDevList)); }

    public bool LdacConflict(bool on) => on && Dual.Ldac && Get<bool>("ldac");

    public void SetDual(bool on, bool turnOffLdac)
    {
        if (Proto == null) return;
        var cfg = Dual;
        if (turnOffLdac)
            Do(Proto.SetQuality(false, Get<bool>("aptx"), Get<bool>("adaptive"), Get<bool>("lossless")), "Turn off LDAC (required for multipoint)", new() { ["ldac"] = false });
        Do(Proto.SetDual(on), $"Multipoint -> {(on ? "On" : "Off")}", new() { ["dual"] = on });
        if ((on && cfg.OpenReset) || cfg.ToggleReset)
        {
            Log("--", null, "This model restarts after the multipoint setting changes and reconnects in about 20 seconds");
            SetStatus(ConnState.Connected, "Headset restarting to apply multipoint settings; reconnecting in about 20 seconds…");
            Note("Headset will restart", "This model restarts after the multipoint setting changes and reconnects in about 20 seconds.", NoticeKind.Info);
            Later(1500, () => { if (Sess != null) Disconnect(keepReconnect: true); SetStatus(ConnState.Connecting, "Waiting for headset to restart…"); });
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
                if (IsConnected && cur == snap) Sess!.Send(frames, $"{note} (retry {i}/2)");
            }
            _dualJobs.Add(Later(3000, () => Again(1)));
            _dualJobs.Add(Later(6000, () => Again(2)));
        }
        DualLater(2000, 5000, 8000, 11000);
    }

    public void DevConnect(PhoneInfo p)
    {
        if (!IsConnected) return;
        if (ConnectedPhones().Count >= 2) { Note("Device limit reached", "The headset is already connected to two devices. Disconnect one first.", NoticeKind.Warning); return; }
        SendDevCmd(Proto!.DevConnect(p.Num), $"Connect device {p.Name}");
    }
    public void DevDisconnect(PhoneInfo p)
    {
        if (!IsConnected) return;
        SendDevCmd(Proto!.DevDisconnect(p.Num), $"Disconnect device {p.Name}");
    }
    public void DevPlay(PhoneInfo p, int state)
    {
        if (!IsConnected) return;
        var fr = Proto!.SetPlay(p.Mac, state);
        if (fr.Count > 0) { Sess!.Send(fr, $"{(state == 1 ? "Set active" : "Pause")} {p.Name}"); DualLater(1500); }
    }

    // Pair a new device.
    public bool Pairing { get; private set; }
    public int PairingLeft { get; private set; }
    public bool? PairingResult { get; private set; }
    int _pairBase; DateTime _pairStart; DispatcherTimer? _pairTick;

    public void StartPairing()
    {
        if (!IsConnected || Pairing) return;
        if (ConnectedPhones().Count >= 2) { Note("Device limit reached", "The headset is already connected to two devices. Disconnect one first.", NoticeKind.Warning); return; }
        Sess!.Send(Proto!.NewConnect(), "Enter pairing mode (connect a new device)");
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
            if (fr.Count > 0 && !ok) Sess!.Send(fr, "Exit pairing mode");
            RefreshDevList();
        }
        if (ok) Note("New device connected", "The headset is connected to the new device.", NoticeKind.Success);
        PairingChanged?.Invoke();
    }
}
