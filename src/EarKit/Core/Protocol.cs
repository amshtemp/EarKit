using static EarKit.Core.Util;
namespace EarKit.Core;

public sealed record PhoneInfo(int Num, string Name, string Mac, int State)
{
    public bool Connected => State == 1;   // Verified on hardware: connectState == 1 means currently connected.
}
public sealed record PlayInfo(string Mac, int Play);

/// <summary>State update collection. Keys match the early prototype (bat_l / noise / keys ...).</summary>
public sealed class StateUpdate : Dictionary<string, object> { }

public abstract class Proto
{
    public Model M { get; }
    protected Proto(Model m) { M = m; }
    public abstract string Family { get; }
    public static Proto Make(Model m) => m.IsQcc ? new QccProto(m) : new EfProto(m);

    public abstract List<byte[]> InitCmds();
    public abstract byte[] Probe();
    public abstract List<byte[]> Queries(bool full);
    public abstract List<byte[]> SetNoise(string mode);
    public abstract List<byte[]> SetSub(string sub);
    public abstract List<byte[]> SetAmb(string a);
    public abstract List<byte[]> SetGame(bool on);
    public abstract List<byte[]> SetDual(bool on);
    public virtual List<byte[]> SetSpatial(bool on) => new();
    public abstract List<byte[]> SetPrompt(int vol);
    public abstract List<byte[]> SetFind(bool l, bool r);
    public abstract List<byte[]> SetMic(string which);
    public abstract List<byte[]> SetQuality(bool ldac, bool aptx, bool adaptive, bool lossless);
    public virtual List<byte[]> SetEar(bool on) => new();
    public abstract List<byte[]> SetAutoOff(int minutes);
    public abstract List<byte[]> SetKey(string side, string gesture, string op, Dictionary<string, string>? keys);
    public abstract List<byte[]> SetDisableKeys(bool l, bool r);
    public abstract List<byte[]> Reset();
    public abstract List<byte[]> DevList();
    public abstract List<byte[]> DevConnect(int num);
    public abstract List<byte[]> DevDisconnect(int num);
    public abstract List<byte[]> NewConnect();
    public abstract List<byte[]> StopNewConnect();
    public abstract List<byte[]> SetPlay(string macHex, int state);
    public abstract List<byte[]> Eq(int[] levels);
    public abstract List<byte[]> KeyQuery();
    public abstract (string Desc, StateUpdate S) Parse(byte[] f);
    public abstract (List<byte[]> Frames, byte[] Remain) SplitFrames(byte[] buf);

    /// Control layout: (side, gesture).
    public virtual List<(string Side, string Gesture)> KeyLayout()
    {
        var l = new List<(string, string)>();
        foreach (var s in new[] { "L", "R" }) foreach (var g in Catalog.GESTURES) l.Add((s, g.Key));
        return l;
    }
    public static string KK(string side, string g) => side + "." + g;
}

// ============================== Qualcomm GAIA ==============================
public sealed class QccProto : Proto
{
    public override string Family => "qcc";
    static readonly Dictionary<string, int> NOISE = new() { ["normal"] = 0, ["anc"] = 1, ["ambient"] = 2, ["adaptive"] = 3, ["wind"] = 4 };
    static readonly Dictionary<string, Dictionary<string, int>> SUB = new()
    {
        ["qcc"] = new() { ["DEFAULT"] = 0, ["WIND"] = 1, ["MODE1"] = 2, ["MODE2"] = 3, ["MODE3"] = 4, ["ADAPTIVE"] = 5, ["BALANCE"] = 6, ["EAR_ADAPTIVE"] = 7 },
        ["tw600"] = new() { ["DEFAULT"] = 2, ["WIND"] = 1, ["BALANCE"] = 3, ["ADAPTIVE"] = 4, ["EAR_ADAPTIVE"] = 0 },
        ["tw700"] = new() { ["WIND"] = 1, ["LEVEL4"] = 2, ["LEVEL3"] = 3, ["ADAPTIVE"] = 4, ["LEVEL2"] = 5, ["LEVEL1"] = 6, ["EAR_ADAPTIVE"] = 0 },
        ["hp1000"] = new() { ["WIND"] = 1, ["LEVEL1"] = 5, ["LEVEL2"] = 4, ["LEVEL3"] = 3, ["LEVEL4"] = 2, ["EAR_ADAPTIVE"] = 6, ["ADAPTIVE"] = 0 },
    };
    static readonly Dictionary<string, int> AMB = new() { ["DEFAULT"] = 0, ["TOTAL"] = 1, ["NATURAL"] = 2 };
    static readonly Dictionary<int, int> AUTOOFF = new() { [5] = 1, [10] = 2, [30] = 3, [60] = 4, [120] = 5 };
    static readonly Dictionary<int, int> AUTOOFF_HP1000 = new() { [90] = 2, [30] = 3, [60] = 4, [120] = 5 };
    static readonly Dictionary<int, string> AUDIO = new() { [1] = "SBC", [2] = "AAC", [3] = "aptX", [4] = "aptX HD", [5] = "aptX Adaptive", [6] = "aptX Lossless", [7] = "LDAC" };
    public const int API = 4, LBAT = 6, RBAT = 7, VER = 9, SET_KEY = 10, KEY = 11, SET_GAME = 18, GAME = 19, SET_NOISE = 20, NOISE_ = 21,
        BOX = 23, COLOR = 24, DUAL = 38, SET_DUAL = 39, SET_QUAL = 46, QUAL = 47, SET_MIC = 48, MIC = 49, SET_FIND = 50,
        FIND = 51, SET_DISKEY = 52, DISKEY = 53, RESET = 54, SET_PVOL = 56, PVOL = 57, SET_SUB = 58, SUB_ = 59, SET_AMB = 60,
        AMB_ = 61, SN = 62, SET_AUTOOFF = 71, AUTOOFF_ = 72, SET_EAR = 73, EAR = 74, AUDIO_ = 77,
        CONN = 41, DISCONN = 42, NEWCONN = 43, DEVLIST = 44, SET_PLAY = 87, PLAYLIST = 88;

    readonly Dictionary<string, int> _sub;
    bool Tw700 => M.Variant is "tw700" or "tw700ai";
    public QccProto(Model m) : base(m)
    {
        var v = m.Variant;
        _sub = SUB[Tw700 ? "tw700" : SUB.ContainsKey(v) ? v : "qcc"];
    }

    public static byte[] Gaia(byte[] body)
    {
        int n = body.Length - 4;
        if (n > 0xFF) return Cat(B(0xFF, 4, 2, (n >> 8) & 0xFF, n & 0xFF), body);
        return Cat(B(0xFF, 4, 0, n), body);
    }
    public byte[] Cmd(int cid, params int[] vals) => Gaia(Cat(B(0x00, 0x0A, 0x03, cid), B(vals)));
    List<byte[]> L(params byte[][] f) => f.ToList();

    public static (List<byte[]>, byte[]) Split(byte[] buf)
    {
        var o = new List<byte[]>();
        while (true)
        {
            int s = Array.IndexOf(buf, (byte)0xFF);
            if (s < 0) return (o, Array.Empty<byte>());
            buf = buf[s..];
            if (buf.Length < 4) return (o, buf);
            if (buf[1] != 3 && buf[1] != 4) { buf = buf[1..]; continue; }
            int flags = buf[2], n, hl;
            if ((flags & 2) != 0) { if (buf.Length < 5) return (o, buf); n = (buf[3] << 8) | buf[4]; hl = 5; }
            else { n = buf[3]; hl = 4; }
            int total = hl + 4 + n + ((flags & 1) != 0 ? 1 : 0);
            if (buf.Length < total) return (o, buf);
            o.Add(buf[..total]); buf = buf[total..];
        }
    }
    public override (List<byte[]> Frames, byte[] Remain) SplitFrames(byte[] buf) => Split(buf);

    public override List<byte[]> InitCmds()
    {
        if (M.Variant is "tw600" or "tw700" or "tw700ai") return new();
        return L(Gaia(B(0, 10, 3, 0)), Gaia(B(0, 0x1D, 0, 1)), Gaia(B(0, 0x1D, 0, 0x0D, 7, 0, 0, 0, 4)),
            Gaia(B(0, 0x1D, 0, 7, 0x0D)), Gaia(B(0, 0x1D, 0, 7, 5)), Gaia(B(0, 0x1D, 0, 7, 8)),
            Gaia(B(0, 0x1D, 0, 7, 1)), Gaia(B(0, 0x1D, 0, 7, 6)), Gaia(B(0, 0x1D, 0, 7, 7)), Gaia(B(0, 0x1D, 0, 7, 7)));
    }
    public override byte[] Probe() => Cmd(VER);

    public override List<byte[]> Queries(bool full)
    {
        var f = M.Feats;
        var q = new List<int> { VER, LBAT };
        if (M.Battery == "tws") q.AddRange(new[] { RBAT, BOX });
        if (!full) { q.Add(NOISE_); return q.Select(i => Cmd(i)).ToList(); }
        q.AddRange(new[] { API, AUDIO_ });
        if (f.Contains("noise")) { q.Add(NOISE_); if (M.Sub.Count > 0) q.Add(SUB_); if (M.Amb.Count > 0) q.Add(AMB_); }
        if (f.Contains("game")) q.Add(GAME);
        if (f.Contains("keys")) q.Add(KEY);
        if (f.Contains("dual")) q.Add(DUAL);
        if (f.Contains("prompt")) q.Add(PVOL);
        if (f.Contains("find")) q.Add(FIND);
        if (f.Contains("mic")) q.Add(MIC);
        if (f.Contains("quality")) q.Add(QUAL);
        if (f.Contains("ear")) q.Add(EAR);
        if (f.Contains("autooff")) q.Add(AUTOOFF_);
        return q.Select(i => Cmd(i)).ToList();
    }

    public override List<byte[]> SetNoise(string mode) => L(Cmd(SET_NOISE, NOISE.GetValueOrDefault(mode)));
    public override List<byte[]> SetSub(string sub) => L(Cmd(SET_SUB, _sub.GetValueOrDefault(sub, 0)));
    public override List<byte[]> SetAmb(string a) => L(Cmd(SET_AMB, AMB.GetValueOrDefault(a)));
    public override List<byte[]> SetGame(bool on) => L(Cmd(SET_GAME, on ? 1 : 0));
    public override List<byte[]> SetDual(bool on) => L(Cmd(SET_DUAL, on ? 1 : 0));
    public override List<byte[]> SetPrompt(int vol) => L(Cmd(SET_PVOL, vol switch { 75 => 1, 50 => 2, 25 => 3, 0 => 4, _ => 0 }));
    public override List<byte[]> SetFind(bool l, bool r) => L(Cmd(SET_FIND, ((r ? 1 : 0) << 1) + (l ? 1 : 0)));
    public override List<byte[]> SetMic(string w) => L(Cmd(SET_MIC, w == "L" ? 1 : w == "R" ? 2 : 0));
    public override List<byte[]> SetQuality(bool ldac, bool aptx, bool adaptive, bool lossless)
        => L(Cmd(SET_QUAL, ((lossless ? 1 : 0) << 4) + ((ldac ? 1 : 0) << 3) + (aptx ? 1 : 0) + ((adaptive ? 1 : 0) << 1)));
    public override List<byte[]> SetEar(bool on)
        => Tw700 ? L(Cmd(SET_EAR, on ? 0x00 : 0x0F)) : L(Cmd(SET_EAR, on ? 0 : 1));
    public override List<byte[]> SetAutoOff(int minutes)
    {
        var mp = M.Variant == "hp1000" ? AUTOOFF_HP1000 : AUTOOFF;
        return L(Cmd(SET_AUTOOFF, mp.TryGetValue(minutes, out var v) ? v : -1));
    }
    public override List<byte[]> SetKey(string side, string gesture, string op, Dictionary<string, string>? keys)
    {
        int t = Catalog.QCC_KEYTYPE.GetValueOrDefault(gesture, 1), v = Catalog.OPS.TryGetValue(op, out var o) ? o.Q : 16;
        return side == "L" ? L(Cmd(SET_KEY, t, v, 0, 0)) : L(Cmd(SET_KEY, 0, 0, t, v));
    }
    public override List<byte[]> SetDisableKeys(bool l, bool r) => L(Cmd(SET_DISKEY, l && r ? 1 : l ? 2 : r ? 3 : 0));
    public override List<byte[]> Reset() => L(Cmd(RESET));
    public override List<byte[]> KeyQuery() => L(Cmd(KEY));

    public override List<byte[]> DevList()
    {
        var q = L(Cmd(DEVLIST));
        if (M.Dual.Play) q.Add(Cmd(PLAYLIST));
        return q;
    }
    public override List<byte[]> DevConnect(int num) => L(Cmd(CONN, num));
    public override List<byte[]> DevDisconnect(int num) => L(Cmd(DISCONN, num));
    public override List<byte[]> NewConnect() => L(Cmd(NEWCONN));
    public override List<byte[]> StopNewConnect() => new();
    public override List<byte[]> SetPlay(string macHex, int state)
    {
        byte[] mac;
        try { mac = Convert.FromHexString(macHex); } catch { return new(); }
        return mac.Length == 3 ? L(Gaia(Cat(B(0, 0x0A, 0x03, SET_PLAY), mac, B(state)))) : new();
    }

    public override List<byte[]> Eq(int[] levels)
    {
        var frames = new List<byte[]>();
        if (M.Eq == "q6")
        {
            var gains = levels.Select(x => x * 60).Concat(new[] { 270, -270, 90, 210 }).ToArray();
            for (int i = 0; i < 10; i++)
            {
                var p = Cat(B(0xA1 + i, 0xFE, 0x98), U16((int)(Catalog.Q6_FREQ[i] * 3)), U16(gains[i]), U16((int)(Catalog.Q6_Q[i] * 4096)));
                frames.Add(Gaia(Cat(B(0, 0x1D, 0x0E, 0x01), p)));
            }
        }
        else
        {
            for (int i = 0; i < levels.Length; i++)
            {
                var p = Cat(B(0xA1 + i, 0xFE, 0x98), U16((int)(Catalog.FREQ10[i] * 3)), U16((int)(Catalog.GAINS[levels[i] + 10] * 60)), U16((int)(0.7 * 4096)));
                frames.Add(Gaia(Cat(B(0, 0x1D, 0x0E, 0x01), p)));
            }
        }
        return frames;
    }

    static readonly Dictionary<int, string> KEYINV = BuildKeyInv();
    static Dictionary<int, string> BuildKeyInv()
    {
        var inv = new Dictionary<int, string>();
        foreach (var k in Catalog.OPS_ORDER) inv.TryAdd(Catalog.OPS[k].Q, k);
        inv[10] = "AMBIENT_NORMAL"; inv[16] = "NOT_SET";
        return inv;
    }

    public override (string, StateUpdate) Parse(byte[] f)
    {
        var s = new StateUpdate();
        int hl = (f[2] & 2) != 0 ? 5 : 4;
        if (f.Length < hl + 2) return ("Short frame", s);
        int vendor = (f[hl] << 8) | f[hl + 1];
        var b = f[(hl + 2)..];
        if (vendor != 0x000A || b.Length < 2) return ($"GAIA vendor {vendor:X4} cmd {Hex(Slice(b, 0, 2))}", s);
        if (b[0] != 0x83 && b[0] != 0x03) return ($"Notification/other {Hex(b)}", s);
        int cid = b[1];
        int? st = b.Length > 2 ? b[2] : null;
        int? valN = b.Length > 3 ? b[3] : null;
        if (st is not null && st != 0 && b.Length <= 3) return ($"Command {cid} returned status {st}", s);
        if (cid == VER) { s["version"] = System.Text.Encoding.UTF8.GetString(Slice(b, 3)).Trim('\0'); return ("", s); }
        if (cid == DEVLIST && b.Length >= 29)
        {
            s["phone"] = new PhoneInfo(b[3], CStr(b[4..23]), Convert.ToHexString(b[25..28]), b[28]);
            return ("", s);
        }
        if (cid == PLAYLIST && b.Length >= 7) { s["play"] = new PlayInfo(Convert.ToHexString(b[3..6]), b[6]); return ("", s); }
        if (valN is null) return ($"Command {cid} acknowledged", s);
        int val = valN.Value;
        switch (cid)
        {
            case LBAT: s["bat_l"] = val; break;
            case RBAT: s["bat_r"] = val; break;
            case BOX:
                if (Tw700) { s["bat_box"] = val & 0x7F; s["box_charging"] = (val & 0x80) != 0; }
                else s["bat_box"] = val;
                break;
            case NOISE_: s["noise"] = val switch { 1 => "anc", 2 => "ambient", 3 => "adaptive", 4 => "wind", _ => "normal" }; break;
            case SUB_:
                var inv = _sub.ToDictionary(kv => kv.Value, kv => kv.Key);
                s["sub"] = inv.TryGetValue(val, out var sv) ? sv : (M.Variant == "tw600" ? "EAR_ADAPTIVE" : $"?{val}");
                break;
            case AMB_: s["amb"] = val switch { 1 => "TOTAL", 2 => "NATURAL", _ => "DEFAULT" }; break;
            case GAME: s["game"] = val != 0; break;
            case DUAL: s["dual"] = val != 0; break;
            case PVOL: s["prompt"] = val switch { 1 => 75, 2 => 50, 3 => 25, 4 => 0, _ => 100 }; break;
            case FIND: s["find_l"] = (val & 1) != 0; s["find_r"] = (val & 2) != 0; break;
            case MIC: s["mic"] = val == 1 ? "L" : val == 2 ? "R" : "AUTO"; break;
            case QUAL:
                s["aptx"] = (val & 1) != 0; s["adaptive"] = (val & 2) != 0; s["ldac"] = (val & 8) != 0; s["lossless"] = (val & 16) != 0; break;
            case EAR: s["ear"] = Tw700 ? (val & 0x0F) != 0x0F : val != 1; break;
            case AUTOOFF_:
                var mp = M.Variant == "hp1000" ? AUTOOFF_HP1000 : AUTOOFF;
                s["autooff"] = mp.FirstOrDefault(kv => kv.Value == val, new KeyValuePair<int, int>(0, 0)).Key; break;
            case AUDIO_: s["codec"] = AUDIO.GetValueOrDefault(val, "?"); break;
            case API: s["api"] = val; break;
            case DISKEY: s["dis_l"] = val is 1 or 2; s["dis_r"] = val is 1 or 3; break;
            case KEY:
                var keys = new Dictionary<string, string>();
                var pos = new (int P, string Side, string G)[] { (4, "L", "ONE"), (6, "R", "ONE"), (8, "L", "DOUBLE"), (10, "R", "DOUBLE"),
                    (12, "L", "TRIPLE"), (14, "R", "TRIPLE"), (16, "L", "LONG"), (18, "R", "LONG") };
                foreach (var (p, side, g) in pos)
                    if (p < b.Length) keys[KK(side, g)] = KEYINV.TryGetValue(b[p], out var op) ? op : $"0x{b[p]:X2}";
                s["keys"] = keys;
                break;
            default: return ($"Command {cid} data {Hex(Slice(b, 2))}", s);
        }
        return ("", s);
    }
}

// ============================== EarFun EF ==============================
public sealed class EfProto : Proto
{
    public override string Family => "ef";
    static readonly Dictionary<string, (int, int)> C = new()
    {
        ["VER"] = (0x10, 0x10), ["COLOR"] = (0x10, 0x12), ["BAT"] = (0x10, 0x14), ["CHG"] = (0x10, 0x16), ["BOX"] = (0x10, 0x18),
        ["BOXCHG"] = (0x10, 0x20), ["AUDIO"] = (0x10, 0x24), ["RESET"] = (0x10, 0x00), ["ALL"] = (0x22, 0x10),
        ["NOISE"] = (0x20, 0x80), ["SET_NOISE"] = (0x20, 0x85), ["GAME"] = (0x20, 0x30), ["SET_GAME"] = (0x20, 0x35),
        ["KEY"] = (0x20, 0x70), ["SET_KEY"] = (0x20, 0x75), ["FIND"] = (0x20, 0x50), ["SET_FIND"] = (0x20, 0x55),
        ["DISKEY"] = (0x20, 0x60), ["SET_DISKEY"] = (0x20, 0x65), ["SET_EQ"] = (0x20, 0x95),
        ["QUAL"] = (0x21, 0x00), ["SET_QUAL"] = (0x21, 0x05), ["LANG"] = (0x21, 0x10), ["PVOL"] = (0x21, 0x20), ["SET_PVOL"] = (0x21, 0x25),
        ["AUTOOFF"] = (0x21, 0x30), ["SET_AUTOOFF"] = (0x21, 0x35), ["DUAL"] = (0x21, 0x40), ["SET_DUAL"] = (0x21, 0x45),
        ["SPATIAL"] = (0x21, 0x90), ["SET_SPATIAL"] = (0x21, 0x95), ["MIC"] = (0x22, 0x00), ["SET_MIC"] = (0x22, 0x05),
        ["AMB"] = (0x23, 0x00), ["SET_AMB"] = (0x23, 0x05), ["SUB"] = (0x23, 0x10), ["SET_SUB"] = (0x23, 0x15),
        ["BASS"] = (0x23, 0x40), ["SET_BASS"] = (0x23, 0x45),
        ["DEVLIST"] = (0x21, 0x50), ["NEWCONN"] = (0x21, 0x85), ["CONN"] = (0x21, 0x65), ["DISCONN"] = (0x21, 0x75),
    };
    static readonly Dictionary<string, int> KEYMAP = new()
    {
        ["NOT_SET"] = 0, ["PLAY_PAUSE"] = 1, ["VOICE_PLUS"] = 2, ["VOICE_MINUS"] = 3, ["PRE"] = 4, ["NEXT"] = 5,
        ["VOICE_ASSISTANT"] = 6, ["GAME_MODE"] = 7, ["NOISE_CONTROL"] = 8, ["ANC_NORMAL"] = 9, ["TURN_OFF"] = 10,
        ["CALL_REPEAT"] = 11, ["DOUBLE_CONNECT"] = 12,
    };
    static readonly Dictionary<int, int> AUTOOFF = new() { [5] = 1, [10] = 2, [30] = 3, [60] = 4, [90] = 5, [120] = 6 };

    readonly Dictionary<string, int> _km;
    readonly Dictionary<int, string> _kinv;
    readonly Dictionary<string, int> _noiseSet;
    readonly Dictionary<int, string> _noiseGet;
    public readonly Dictionary<string, string> Keys = new();

    public EfProto(Model m) : base(m)
    {
        var v = m.Variant;
        _km = new Dictionary<string, int>(KEYMAP);
        if (v == "tw230") { _km.Remove("DOUBLE_CONNECT"); _km["SPATIAL_MODE"] = 12; _km["AMBIENT_NORMAL"] = 14; _km["ANC_AMBIENT"] = 15; }
        else if (v is "cw100" or "cw200") { _km.Remove("DOUBLE_CONNECT"); _km["BASS_EHANCEMENT"] = 12; _km["MANNER_MODE"] = 13; }
        _kinv = new();
        foreach (var kv in _km) _kinv[kv.Value] = kv.Key;   // Later entries take precedence.
        if (v == "tw230") { _noiseSet = new() { ["normal"] = 1, ["ambient"] = 2, ["anc"] = 3 }; }
        else if (v == "head") { _noiseSet = new() { ["normal"] = 1, ["ambient"] = 2, ["wind"] = 3, ["anc2"] = 4, ["anc1"] = 5 }; }
        else { _noiseSet = new() { ["normal"] = 1, ["ambient"] = 2, ["wind"] = 3, ["anc"] = 4, ["adaptive"] = 5 }; }
        _noiseGet = _noiseSet.ToDictionary(kv => kv.Value, kv => kv.Key);
    }

    public static byte[] Frame((int, int) cmd, byte[]? payload = null)
    {
        payload ??= Array.Empty<byte>();
        int sum = payload.Length + payload.Sum(x => (int)x);
        return Cat(B(0xEF, cmd.Item1, cmd.Item2, payload.Length), payload, B(sum & 0xFF, 0xFE));
    }
    public byte[] Cmd(string name, params int[] vals) => Frame(C[name], B(vals));
    List<byte[]> L(params byte[][] f) => f.ToList();

    public static (List<byte[]>, byte[]) Split(byte[] buf)
    {
        var o = new List<byte[]>();
        while (true)
        {
            int s = Array.IndexOf(buf, (byte)0xEF);
            if (s < 0) return (o, Array.Empty<byte>());
            buf = buf[s..];
            if (buf.Length < 6) return (o, buf);
            int total = buf[3] + 6;
            if (buf.Length < total) return (o, buf);
            var fr = buf[..total];
            if (fr[^1] != 0xFE) { buf = buf[1..]; continue; }
            o.Add(fr); buf = buf[total..];
        }
    }
    public override (List<byte[]> Frames, byte[] Remain) SplitFrames(byte[] buf) => Split(buf);

    public override List<byte[]> InitCmds() => new();
    public override byte[] Probe() => Cmd("VER");

    public override List<byte[]> Queries(bool full)
    {
        var f = M.Feats;
        var q = new List<string> { "VER", "BAT" };
        if (M.Battery == "tws") q.AddRange(new[] { "BOX", "CHG" });
        if (!full) { q.Add("NOISE"); return q.Select(x => Cmd(x)).ToList(); }
        q.Add("AUDIO");
        if (f.Contains("noise")) { q.Add("NOISE"); if (M.Amb.Count > 0) q.Add("AMB"); }
        if (f.Contains("game")) q.Add("GAME");
        if (f.Contains("keys")) q.Add("KEY");
        if (f.Contains("dual")) q.Add("DUAL");
        if (f.Contains("prompt")) q.Add("PVOL");
        if (f.Contains("find")) q.Add("FIND");
        if (f.Contains("quality")) q.Add("QUAL");
        if (f.Contains("autooff")) q.Add("AUTOOFF");
        if (f.Contains("spatial")) q.Add("SPATIAL");
        return q.Select(x => Cmd(x)).ToList();
    }

    public override List<byte[]> SetNoise(string mode) => L(Cmd("SET_NOISE", _noiseSet.GetValueOrDefault(mode, 1)));
    public override List<byte[]> SetSub(string sub)
    {
        if (M.Variant == "tw230") return L(Cmd("SET_SUB", sub switch { "BALANCE" => 2, "WIND" => 3, "ADAPTIVE" => 4, _ => 1 }));
        return L(Cmd("SET_NOISE", sub switch { "MODE1" => 4, "WIND" => 3, "ADAPTIVE" => 5, _ => 1 }));
    }
    public override List<byte[]> SetAmb(string a) => L(Cmd("SET_AMB", a == "NATURAL" ? 2 : 1));
    public override List<byte[]> SetGame(bool on) => L(Cmd("SET_GAME", on ? 2 : 1));
    public override List<byte[]> SetDual(bool on) => L(Cmd("SET_DUAL", on ? 2 : 1));
    public override List<byte[]> SetSpatial(bool on) => L(Cmd("SET_SPATIAL", on ? 2 : 1));
    public override List<byte[]> SetPrompt(int vol) => L(Cmd("SET_PVOL", vol switch { 100 => 5, 75 => 4, 50 => 3, 25 => 2, _ => 1 }));
    public override List<byte[]> SetFind(bool l, bool r) => L(Cmd("SET_FIND", l ? 1 : 0, r ? 1 : 0));
    public override List<byte[]> SetMic(string w) => L(Cmd("SET_MIC", w == "L" ? 1 : w == "R" ? 2 : 0));
    public override List<byte[]> SetQuality(bool ldac, bool aptx, bool adaptive, bool lossless)
        => L(Cmd("SET_QUAL", ((ldac ? 1 : 0) << 2) + (aptx ? 1 : 0) + ((adaptive ? 1 : 0) << 1)));
    public override List<byte[]> SetAutoOff(int minutes) => L(Cmd("SET_AUTOOFF", AUTOOFF.TryGetValue(minutes, out var v) ? v : -1));
    public override List<byte[]> SetDisableKeys(bool l, bool r) => L(Cmd("SET_DISKEY", l ? 1 : 0, r ? 1 : 0));
    public override List<byte[]> Reset() => L(Frame(C["RESET"], B(0xAF, 0xA5)));
    public override List<byte[]> KeyQuery() => L(Cmd("KEY"));

    public override List<byte[]> DevList() => L(Cmd("DEVLIST"));
    public override List<byte[]> DevConnect(int num) => L(Cmd("CONN", num));
    public override List<byte[]> DevDisconnect(int num) => L(Cmd("DISCONN", num));
    public override List<byte[]> NewConnect() => L(Cmd("NEWCONN", 0));
    public override List<byte[]> StopNewConnect() => L(Cmd("NEWCONN", 1));
    public override List<byte[]> SetPlay(string macHex, int state) => new();

    public override List<(string Side, string Gesture)> KeyLayout()
    {
        var v = M.Variant;
        if (v == "head") return new() { ("PLUS", "ONE"), ("PLUS", "LONG"), ("MINUS", "ONE"), ("MINUS", "LONG"), ("NC", "ONE"), ("NC", "LONG") };
        var lay = new List<(string, string)> { ("L", "ONE"), ("L", "DOUBLE"), ("L", "TRIPLE"), ("L", "LONG"), ("R", "ONE"), ("R", "DOUBLE"), ("R", "TRIPLE"), ("R", "LONG") };
        if (v == "cw200") lay.AddRange(new[] { ("L", "CALL_TRIPLE"), ("L", "CALL_LONG"), ("R", "CALL_TRIPLE"), ("R", "CALL_LONG") });
        return lay;
    }

    public override List<byte[]> SetKey(string side, string gesture, string op, Dictionary<string, string>? keys)
    {
        if (keys != null) foreach (var kv in keys) Keys[kv.Key] = kv.Value;
        Keys[KK(side, gesture)] = op;
        var pl = new List<int>();
        foreach (var (s, g) in KeyLayout())
        {
            var cur = Keys.GetValueOrDefault(KK(s, g), "NOT_SET");
            if (cur.StartsWith("0x")) pl.Add(Convert.ToInt32(cur[2..], 16));
            else pl.Add(_km.GetValueOrDefault(cur, 0));
        }
        return L(Cmd("SET_KEY", pl.ToArray()));
    }

    public override List<byte[]> Eq(int[] levels)
    {
        var e = M.Eq;
        int mx = levels.Max();
        double gmax = Catalog.GAINS[mx + 10];
        byte[] master = e switch
        {
            "ef_fixed" or "ef_tw205" => U16(0xFE98),
            "ef_hp201" => U16((int)(60 * (-2 - Math.Max(gmax, 0)))),
            "ef_hp220" => U16((int)(-8 - Math.Max(gmax, 0)) * 60),
            "ef_cw100" => U16((int)(60 * (-5 - Math.Max(gmax, 0)))),
            "ef_cw200" => U16((int)(60 * (gmax > 0 ? -gmax : 0))),
            "ef_ow100" => U16((int)(60 * Catalog.OW100_MASTER[mx + 10])),
            "ef_tw230" => U16((int)(60 * Catalog.TW230_MASTER[mx + 10])),
            _ => U16(0xFE98),
        };
        var freqs = e == "ef_tw205" ? Catalog.FREQ_TW205 : Catalog.FREQ10;
        var frames = new List<byte[]>();
        for (int i = 0; i < levels.Length; i++)
        {
            var p = Cat(B(levels.Length, i + 1), master, U16((int)(freqs[i] * 3)), U16((int)(Catalog.GAINS[levels[i] + 10] * 60)), U16((int)(0.7 * 4096)));
            frames.Add(Frame(C["SET_EQ"], p));
        }
        return frames;
    }

    public override (string, StateUpdate) Parse(byte[] f)
    {
        var s = new StateUpdate();
        var cmd = ((int)f[1], (int)f[2]);
        var p = Slice(f, 4, 4 + f[3]);
        int? v0n = p.Length > 0 ? p[0] : null;
        var name = C.FirstOrDefault(kv => kv.Value == cmd).Key ?? $"{cmd.Item1:X2}{cmd.Item2:X2}";
        bool Is(string n) => C[n] == cmd;
        if (Is("VER"))
        {
            var txt = p.Where(x => x != 0).ToArray();
            if (M.Variant is "tw230" or "cw100" or "cw200" or "head" || (txt.Length > 3 && txt.All(c => c >= 32 && c < 127)))
                s["version"] = System.Text.Encoding.UTF8.GetString(txt);
            else if (p.Length >= 3) s["version"] = $"{p[0]}.{p[1]}.{p[2]}";
            return ("", s);
        }
        if (Is("DEVLIST") && p.Length >= 26)
        {
            s["phone"] = new PhoneInfo(p[0], CStr(p[1..20]), Convert.ToHexString(p[21..25]), p[25]);
            return ("", s);
        }
        if (v0n is null) return ($"{name} acknowledged", s);
        int v0 = v0n.Value;
        if (Is("BAT")) { s["bat_l"] = (int)p[0]; if (M.Battery == "tws" && p.Length > 1) s["bat_r"] = (int)p[1]; }
        else if (Is("BOX")) s["bat_box"] = v0;
        else if (Is("CHG")) { s["chg_l"] = (v0 & 2) != 0; s["chg_r"] = (v0 & 8) != 0; }
        else if (Is("BOXCHG")) s["box_charging"] = v0 != 0;
        else if (Is("NOISE")) s["noise"] = _noiseGet.GetValueOrDefault(v0, "normal");
        else if (Is("SUB") && M.Variant == "tw230") s["sub"] = v0 switch { 2 => "BALANCE", 3 => "WIND", 4 => "ADAPTIVE", _ => "DEFAULT" };
        else if (Is("AMB")) s["amb"] = v0 == 2 ? "NATURAL" : "DEFAULT";
        else if (Is("GAME")) s["game"] = v0 == 2;
        else if (Is("DUAL")) s["dual"] = v0 == 2;
        else if (Is("SPATIAL")) s["spatial"] = v0 == 2;
        else if (Is("PVOL")) s["prompt"] = v0 switch { 5 => 100, 4 => 75, 3 => 50, 2 => 25, _ => 0 };
        else if (Is("FIND")) { s["find_l"] = p[0] != 0; s["find_r"] = p.Length > 1 && p[1] != 0; }
        else if (Is("MIC")) s["mic"] = v0 == 1 ? "L" : v0 == 2 ? "R" : "AUTO";
        else if (Is("QUAL")) { s["aptx"] = (v0 & 1) != 0; s["adaptive"] = (v0 & 2) != 0; s["ldac"] = (v0 & 4) != 0; }
        else if (Is("AUTOOFF")) s["autooff"] = AUTOOFF.FirstOrDefault(kv => kv.Value == v0, new KeyValuePair<int, int>(0, 0)).Key;
        else if (Is("AUDIO")) s["codec"] = v0 switch { 2 => "AAC", 3 => "aptX", 4 => "aptX HD", 5 => "aptX Adaptive", 6 => "aptX Lossless", 7 => "LDAC", _ => "SBC" };
        else if (Is("DISKEY")) { s["dis_l"] = p[0] != 0; s["dis_r"] = p.Length > 1 && p[1] != 0; }
        else if (Is("KEY"))
        {
            var keys = new Dictionary<string, string>();
            var lay = KeyLayout();
            for (int i = 0; i < lay.Count && i < p.Length; i++)
                keys[KK(lay[i].Side, lay[i].Gesture)] = _kinv.TryGetValue(p[i], out var op) ? op : $"0x{p[i]:X2}";
            foreach (var kv in keys) Keys[kv.Key] = kv.Value;
            s["keys"] = keys;
        }
        else return ($"{name} data {Hex(p)}", s);
        return ("", s);
    }
}
