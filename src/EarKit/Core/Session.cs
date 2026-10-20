using System.Collections.Concurrent;
namespace EarKit.Core;

public delegate void LogFn(string dir, byte[]? data, string text);

/// 负责收发线程、命令队列（40ms 间隔）、帧切分。回调在后台线程触发。
public sealed class Session : IDisposable
{
    public ILink Link { get; }
    public Model Model { get; private set; }
    public Proto Proto { get; private set; }
    public readonly Dictionary<string, object> State = new();
    public volatile bool Alive = true;
    public DateTime LastRx;
    readonly LogFn _log;
    readonly Action<StateUpdate> _onState;
    readonly BlockingCollection<(byte[] F, string Note)> _q = new();
    readonly int _gapMs;
    byte[] _buf = Array.Empty<byte>();

    public Session(ILink link, Model model, LogFn log, Action<StateUpdate> onState, int gapMs = 40)
    {
        Link = link; Model = model; Proto = Proto.Make(model); _log = log; _onState = onState; _gapMs = gapMs;
        new Thread(Rx) { IsBackground = true, Name = "SessRx" }.Start();
        new Thread(Tx) { IsBackground = true, Name = "SessTx" }.Start();
    }

    public void SetModel(Model m) { lock (this) { Model = m; Proto = Proto.Make(m); } }

    public void Send(IEnumerable<byte[]> frames, string note = "")
    {
        bool first = true;
        foreach (var f in frames) { if (Alive) _q.Add((f, first ? note : "")); first = false; }
    }
    public void SendCombined(IEnumerable<byte[]> frames, string note = "")
    {
        var all = frames.SelectMany(x => x).ToArray();
        if (all.Length > 0 && Alive) _q.Add((all, note));
    }

    void Tx()
    {
        while (Alive)
        {
            if (!_q.TryTake(out var it, 200)) continue;
            try { Link.Write(it.F); _log(">>", it.F, it.Note); }
            catch (Exception e)
            {
                if (!Alive) break;
                _log("!!", null, "发送失败: " + e.Message);
                Alive = false;
                _onState(new StateUpdate { ["_disconnected"] = e.Message });
                break;
            }
            Thread.Sleep(_gapMs);
        }
    }

    void Rx()
    {
        while (Alive)
        {
            byte[] d;
            try { d = Link.Read(); }
            catch (Exception e)
            {
                if (Alive)
                {
                    _log("!!", null, "接收失败: " + e.Message);
                    Alive = false;
                    _onState(new StateUpdate { ["_disconnected"] = e.Message });
                }
                break;
            }
            if (d.Length == 0) continue;
            LastRx = DateTime.Now;
            _buf = Util.Cat(_buf, d);
            var mine = Proto;
            var tryOrder = mine is QccProto ? new[] { "qcc", "ef" } : new[] { "ef", "qcc" };
            foreach (var fam in tryOrder)
            {
                var (frames, rest) = fam == "qcc" ? QccProto.Split(_buf) : EfProto.Split(_buf);
                if (frames.Count == 0) continue;
                _buf = rest;
                foreach (var fr in frames) Handle(fam, fr);
                break;
            }
            if (_buf.Length > 4096) { _log("<<", _buf, "无法识别的数据（已丢弃）"); _buf = Array.Empty<byte>(); }
        }
    }

    void Handle(string fam, byte[] fr)
    {
        var p = Proto;
        if (fam != p.Family)
        {
            _log("<<", fr, $"收到{(fam == "qcc" ? "高通 GAIA" : "EarFun EF")}帧——型号/协议可能选错了");
            _onState(new StateUpdate { ["_wrong_family"] = fam });
            return;
        }
        string desc; StateUpdate upd;
        try { (desc, upd) = p.Parse(fr); }
        catch (Exception e) { desc = "解析异常 " + e.Message; upd = new StateUpdate(); }
        if (upd.Count > 0)
        {
            lock (State) foreach (var kv in upd) State[kv.Key] = kv.Value;
            _onState(upd);
            if (string.IsNullOrEmpty(desc)) desc = string.Join(", ", upd.Select(kv => $"{kv.Key}={Fmt(kv.Value)}"));
        }
        _log("<<", fr, desc);
    }

    static string Fmt(object v) => v switch
    {
        Dictionary<string, string> d => "{" + string.Join(", ", d.Select(kv => $"{kv.Key}:{kv.Value}")) + "}",
        bool b => b ? "True" : "False",
        _ => v?.ToString() ?? "null",
    };

    public void Dispose()
    {
        Alive = false;
        try { Link.Dispose(); } catch { }
    }

    /// 依次发送两种协议的"读固件版本"，看哪种有回应。返回 (family, version)
    public static (string? Family, string? Version) ProbeFamily(ILink link, LogFn? log, int timeoutMs = 1500)
    {
        var dq = new Model("_", "_", "qcc", "qcc", "q10", "");
        var de = new Model("_", "_", "ef", "ef", "ef_fixed", "");
        foreach (Proto P in new Proto[] { new QccProto(dq), new EfProto(de) })
        {
            var f = P.Probe();
            try { link.Write(f); }
            catch (Exception e) { log?.Invoke("!!", null, "探测发送失败 " + e.Message); return (null, null); }
            log?.Invoke(">>", f, "探测 " + P.Family);
            var buf = Array.Empty<byte>();
            var end = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < end)
            {
                try { buf = Util.Cat(buf, link.Read()); } catch { break; }
                var (frames, rest) = P.SplitFrames(buf);
                buf = rest;
                foreach (var fr in frames)
                {
                    log?.Invoke("<<", fr, "探测回应");
                    StateUpdate s;
                    try { s = P.Parse(fr).S; } catch { s = new StateUpdate(); }
                    if (s.TryGetValue("version", out var v)) return (P.Family, v as string);
                }
                if (frames.Count > 0) return (P.Family, "");
            }
        }
        return (null, null);
    }
}
