namespace EarKit.Core;

public static class Util
{
    public static byte[] U16(int v) { v &= 0xFFFF; return new[] { (byte)(v >> 8), (byte)(v & 0xFF) }; }
    public static string Hex(byte[]? b) => b == null ? "" : string.Join(" ", b.Select(x => x.ToString("X2")));
    public static byte[] Cat(params byte[][] parts) { var l = new List<byte>(); foreach (var p in parts) l.AddRange(p); return l.ToArray(); }
    public static byte[] B(params int[] v) => v.Select(x => (byte)(x & 0xFF)).ToArray();
    public static byte[] Slice(byte[] b, int start, int end = int.MaxValue)
    {
        if (start >= b.Length) return Array.Empty<byte>();
        end = Math.Min(end, b.Length);
        return end <= start ? Array.Empty<byte>() : b[start..end];
    }
    public static string CStr(byte[] b)
    {
        int z = Array.IndexOf(b, (byte)0);
        if (z >= 0) b = b[..z];
        return System.Text.Encoding.UTF8.GetString(b).Trim();
    }
    public static byte[]? ParseHex(string s)
    {
        s = s.Replace(",", " ").Replace("0x", " ").Replace("0X", " ");
        var parts = s.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var str = string.Concat(parts);
        if (str.Length % 2 != 0) return null;
        try { return Convert.FromHexString(str); } catch { return null; }
    }
    public static string FmtMac(ulong a) => string.Join(":", Enumerable.Range(0, 6).Select(i => ((a >> (8 * (5 - i))) & 0xFF).ToString("X2")));
    public static ulong ParseMac(string mac) => Convert.ToUInt64(mac.Replace(":", "").Replace("-", ""), 16);
}

public sealed class DualCfg
{
    public bool List = true, ShowOff = true, Resend = true, Play, Ldac, OpenReset, ToggleReset;
}

public sealed class Model
{
    public string Key, Name, Family, Variant, Eq;
    public HashSet<string> Feats;
    public List<string> Noise, Sub, Amb;
    public List<int> AutoOff;
    public (int, int) Pid;
    public string Battery;
    public bool CombineEq;
    public List<string>? Ops;
    public string[] Aliases;
    public DualCfg Dual;

    public Model(string key, string name, string family, string variant, string eq, string feats,
        string[]? noise = null, string[]? sub = null, string[]? amb = null, int[]? autooff = null,
        (int, int) pid = default, string battery = "tws", bool combineEq = false, List<string>? ops = null, string[]? aliases = null)
    {
        Key = key; Name = name; Family = family; Variant = variant; Eq = eq;
        Feats = new HashSet<string>(feats.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        Noise = (noise ?? Array.Empty<string>()).ToList(); Sub = (sub ?? Array.Empty<string>()).ToList();
        Amb = (amb ?? Array.Empty<string>()).ToList(); AutoOff = (autooff ?? Array.Empty<int>()).ToList();
        Pid = pid; Battery = battery; CombineEq = combineEq; Ops = ops; Aliases = aliases ?? Array.Empty<string>();
        Dual = Catalog.MakeDual(key);
    }
    public bool IsQcc => Family == "qcc";
    public bool IsHead => Battery == "head";
    public int EqBands => Eq == "q6" ? 6 : 10;
    public List<string> KeyOps => Ops ?? (IsQcc ? Catalog.Q_OPS : Catalog.EF_OPS);
    public override string ToString() => $"{Name} ({Key})";
}

public static class Catalog
{
    public static readonly double[] GAINS = { -6, -5, -4, -3.5, -3, -2.5, -2, -1.5, -1, -0.5, 0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5, 4, 5, 6 };
    public static readonly double[] FREQ10 = { 31.5, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    public static readonly double[] FREQ_TW205 = { 32, 63, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    public static readonly double[] Q6_FREQ = { 63, 180, 500, 1000, 8000, 15000, 33, 200, 2000, 15000 };
    public static readonly double[] Q6_Q = { 0.7, 0.6, 0.6, 0.6, 0.6, 0.6, 0.5, 0.6, 0.9, 0.6 };
    public static readonly double[] OW100_MASTER = Enumerable.Repeat(-0.5, 11).Concat(new[] { -1.0, -1.5, -2.0, -2.5, -3.0, -3.5, -4.0, -4.5, -5.5, -6.5 }).ToArray();
    public static readonly double[] TW230_MASTER = Enumerable.Repeat(-11.0, 17).Concat(new[] { -11.5, -12.0, -13.0, -14.0 }).ToArray();

    public static readonly (string Name, int[] V)[] EQ_PRESETS_10 =
    {
        ("Default (Flat)", new[]{0,0,0,0,0,0,0,0,0,0}), ("Rock", new[]{4,5,2,0,0,1,2,3,3,2}), ("Pop", new[]{3,4,0,0,0,0,2,3,3,3}),
        ("Country", new[]{0,1,0,0,1,0,0,2,1,2}), ("R&B", new[]{2,3,1,0,0,0,1,2,0,1}),
        ("Classical", new[]{-3,-2,-1,1,1,0,1,1,2,3}), ("Jazz", new[]{0,0,0,1,1,0,1,2,1,2}),
        ("Electronic", new[]{1,2,0,0,2,0,2,4,6,4}), ("Dance", new[]{0,4,0,0,2,0,2,4,1,0}),
        ("Hip-hop", new[]{2,4,0,0,3,2,1,7,0,0}), ("Metal", new[]{0,2,0,2,3,0,2,4,6,7}),
        ("Blues", new[]{0,0,0,1,2,0,1,3,1,2}), ("Soul", new[]{3,3,1,0,0,0,1,2,2,2}),
        ("Popular", new[]{2,4,1,-1,0,3,1,3,0,0}), ("Latin", new[]{3,3,0,0,0,3,0,2,0,2}),
        ("Natural", new[]{-5,-4,-2,0,-1,-1,0,-1,0,0}), ("Vocal boost", new[]{0,0,0,1,4,5,4,2,0,0}),
        ("Vocal reduction", new[]{0,0,0,-1,-4,-5,-4,-2,0,0}), ("Bass boost 1", new[]{2,3,0,0,0,0,0,0,0,0}),
        ("Bass boost 2", new[]{3,6,0,0,0,0,0,0,0,0}), ("Bass boost 3", new[]{4,9,0,0,0,0,0,0,0,0}),
        ("Bass reduction 1", new[]{-4,-3,-2,0,0,0,0,0,0,0}), ("Bass reduction 2", new[]{-8,-6,-3,0,0,0,0,0,0,0}),
        ("Treble boost 1", new[]{0,0,0,0,0,0,0,2,3,3}), ("Treble boost 2", new[]{0,0,0,0,0,0,0,3,6,6}),
        ("Treble reduction 1", new[]{0,0,0,0,0,0,0,-2,-3,-3}), ("Treble reduction 2", new[]{0,0,0,0,0,0,0,-3,-6,-6}),
    };
    public static readonly (string Name, int[] V)[] EQ_PRESETS_6 =
    {
        ("Default (Flat)", new[]{0,0,0,0,0,0}), ("Bass boost", new[]{8,0,0,0,0,0}), ("Treble boost", new[]{0,0,0,0,0,6}),
        ("Bass reduction", new[]{-6,-3,2,0,0,0}), ("Treble reduction", new[]{0,0,0,-2,-6,-6}),
    };

    public static readonly Dictionary<string, string> NOISE_LABEL = new()
    {
        ["normal"] = "Off", ["anc"] = "Noise cancellation", ["ambient"] = "Ambient", ["adaptive"] = "Adaptive",
        ["wind"] = "Wind reduction", ["anc1"] = "Noise cancellation ANC1", ["anc2"] = "Noise cancellation ANC2",
    };
    public static readonly Dictionary<string, string> SUB_LABEL = new()
    {
        ["DEFAULT"] = "Strong noise cancellation", ["BALANCE"] = "Balanced noise cancellation", ["ADAPTIVE"] = "Adaptive noise cancellation",
        ["EAR_ADAPTIVE"] = "Ear-adaptive", ["WIND"] = "Wind reduction", ["LEVEL1"] = "Noise cancellation level 1",
        ["LEVEL2"] = "Noise cancellation level 2", ["LEVEL3"] = "Noise cancellation level 3", ["LEVEL4"] = "Noise cancellation level 4 (strongest)", ["MODE1"] = "Standard",
    };
    public static readonly Dictionary<string, string> AMB_LABEL = new() { ["DEFAULT"] = "Default", ["TOTAL"] = "Natural", ["NATURAL"] = "Natural" };
    public static string AutoOffLabel(int m) => m == 0 ? "Never" : $"{m} minutes";
    public static readonly int[] PROMPT_VOL = { 100, 75, 50, 25, 0 };

    public static readonly Dictionary<string, (string Label, int Q)> OPS = new()
    {
        ["PLAY_PAUSE"] = ("Play / pause", 1), ["VOICE_PLUS"] = ("Volume +", 2), ["VOICE_MINUS"] = ("Volume -", 3),
        ["PRE"] = ("Previous track", 4), ["NEXT"] = ("Next track", 5), ["VOICE_ASSISTANT"] = ("Voice assistant", 6),
        ["GAME_MODE"] = ("Game mode", 7), ["NOISE_CONTROL"] = ("Cycle noise control", 8), ["ANC_NORMAL"] = ("Toggle noise cancellation / off", 9),
        ["AMBIENT_NORMAL"] = ("Toggle ambient / off", 10), ["ANC_AMBIENT"] = ("Toggle noise cancellation / ambient", 11), ["TURN_OFF"] = ("Power off", 12),
        ["CALL_REPEAT"] = ("Redial", 13), ["VOICE_COMMOND"] = ("Voice command", 14), ["MANNER_MODE"] = ("Manner mode", 18),
        ["BASS_EHANCEMENT"] = ("Bass boost", 19), ["SPATIAL_MODE"] = ("Spatial audio", 51), ["DOUBLE_CONNECT"] = ("Multipoint", 10),
        ["NOT_SET"] = ("Unassigned", 16),
    };
    // Keep OPS keys in declaration order without relying on dictionary enumeration behavior.
    public static readonly string[] OPS_ORDER = { "PLAY_PAUSE", "VOICE_PLUS", "VOICE_MINUS", "PRE", "NEXT", "VOICE_ASSISTANT", "GAME_MODE",
        "NOISE_CONTROL", "ANC_NORMAL", "AMBIENT_NORMAL", "ANC_AMBIENT", "TURN_OFF", "CALL_REPEAT", "VOICE_COMMOND", "MANNER_MODE",
        "BASS_EHANCEMENT", "SPATIAL_MODE", "DOUBLE_CONNECT", "NOT_SET" };
    public static string OpLabel(string op) => OPS.TryGetValue(op, out var o) ? o.Label : op;
    public static readonly (string Key, string Label)[] GESTURES = { ("ONE", "Single tap"), ("DOUBLE", "Double tap"), ("TRIPLE", "Triple tap"), ("LONG", "Long press") };
    public static readonly Dictionary<string, int> QCC_KEYTYPE = new() { ["ONE"] = 1, ["DOUBLE"] = 2, ["TRIPLE"] = 3, ["LONG"] = 17 };

    public static DualCfg MakeDual(string key)
    {
        var d = new DualCfg();
        switch (key)
        {
            case "TW600": d.Ldac = true; d.OpenReset = true; break;
            case "TW700": case "TW700AI": case "TW800": case "HP1000": d.Play = true; d.Ldac = true; d.OpenReset = true; break;
            case "TW220": case "TW220_VDL": d.Ldac = true; d.List = false; break;
            case "TW230": d.Ldac = true; d.List = false; d.Resend = false; d.OpenReset = true; break;
            case "OW100": case "OW100_NEW": d.Ldac = true; break;
            case "CW100": d.Ldac = true; d.ToggleReset = true; break;
            case "CW200": d.Ldac = true; d.Resend = false; d.OpenReset = true; break;
            case "HP201": d.ShowOff = false; break;
        }
        return d;
    }

    static readonly string[] N3 = { "normal", "anc", "ambient" };
    public static readonly int[] AO6 = { 5, 10, 30, 60, 120, 0 };
    public static readonly int[] AO7 = { 5, 10, 30, 60, 90, 120, 0 };
    public static readonly List<string> Q_OPS = new() { "PLAY_PAUSE", "VOICE_PLUS", "VOICE_MINUS", "PRE", "NEXT", "VOICE_ASSISTANT", "GAME_MODE",
        "NOISE_CONTROL", "ANC_NORMAL", "AMBIENT_NORMAL", "ANC_AMBIENT", "CALL_REPEAT", "NOT_SET" };
    public static readonly List<string> EF_OPS = new() { "PLAY_PAUSE", "VOICE_PLUS", "VOICE_MINUS", "PRE", "NEXT", "VOICE_ASSISTANT", "GAME_MODE",
        "NOISE_CONTROL", "ANC_NORMAL", "TURN_OFF", "CALL_REPEAT", "DOUBLE_CONNECT", "NOT_SET" };
    const string QF = "battery version noise game eq keys dual prompt find mic quality ear reset";
    static List<string> EfOpsPlus(params string[] extra) => EF_OPS.Take(EF_OPS.Count - 2).Concat(extra).ToList();

    static readonly string[] SUB_TW700 = { "EAR_ADAPTIVE", "ADAPTIVE", "WIND", "LEVEL1", "LEVEL2", "LEVEL3", "LEVEL4" };
    static readonly string[] AMB_DT = { "DEFAULT", "TOTAL" };
    const string Q6F = "battery version noise game eq keys reset";
    const string EFF = "battery version game eq keys dual prompt find quality autooff spatial reset";

    public static readonly List<Model> MODELS = new()
    {
        // ---------------- Qualcomm GAIA ----------------
        new("TW600", "EarFun Air Pro 4", "qcc", "tw600", "q10", QF, N3, new[]{"DEFAULT","BALANCE","ADAPTIVE","EAR_ADAPTIVE","WIND"}, AMB_DT, AO6, (0x45,0x35)),
        new("TW700", "EarFun Air Pro 4+", "qcc", "tw700", "q10", QF + " autooff", N3, SUB_TW700, AMB_DT, AO6, (0x45,0x38)),
        new("TW700AI", "EarFun Air Pro 4+AI", "qcc", "tw700ai", "q10", QF + " autooff", N3, SUB_TW700, AMB_DT, AO6, (0x45,0x39)),
        new("TW800", "EarFun Air Pro 5", "qcc", "tw700", "q10", QF + " autooff", N3, SUB_TW700, AMB_DT, AO6, (0xC0,0x0E)),
        new("TW500", "EarFun Air Pro 3", "qcc", "qcc", "q10", QF, N3, new[]{"DEFAULT","WIND"}, null, AO6, (0x45,0x32)),
        new("TW500_3072", "EarFun Air Pro 3 (3072)", "qcc", "qcc", "q10", QF, N3, new[]{"DEFAULT","WIND"}, null, AO6, (0x45,0x33), aliases: new[]{"EarFun Air Pro 3"}),
        new("TW400", "EarFun Free Pro 3", "qcc", "qcc", "q10", QF, N3, new[]{"DEFAULT","WIND"}, null, AO6, (0x45,0x34)),
        new("TWA01", "EarFun Air S", "qcc", "qcc", "q6", Q6F, N3, null, null, null, (0x41,0x31)),
        new("TW201", "EarFun Air S (TW201)", "qcc", "qcc", "q6", Q6F, N3, null, null, null, (0x4C,0x45), aliases: new[]{"EarFun Air S"}),
        new("ATVEL_TW201", "ATVEL Tune S5 ProX", "qcc", "qcc", "q6", Q6F, N3, null, null, null, (0x41,0x31)),
        new("TW101S", "EarFun Free 2S", "qcc", "qcc", "q6", "battery version game eq keys reset", null, null, null, null, (0x45,0x37)),
        new("HP1000", "EarFun Wave Pro X", "qcc", "hp1000", "q10", "battery version noise game eq dual prompt quality autooff reset", N3,
            SUB_TW700, AMB_DT, new[]{30,60,90,120,0}, (0x45,0x3A), battery: "head"),
        // ---------------- EarFun EF protocol ----------------
        new("TW220", "EarFun Air 2 NC", "ef", "ef", "ef_fixed", "battery version noise game eq keys dual prompt find quality autooff spatial reset",
            new[]{"normal","ambient","wind","adaptive","anc"}, null, null, AO7, (0x70,0x33)),
        new("TW220_VDL", "EarFun Air 2 NC (VDL)", "ef", "ef", "ef_fixed", "battery version noise game eq keys dual prompt find quality autooff spatial reset",
            new[]{"normal","ambient","wind","adaptive","anc"}, null, null, AO7, (0x70,0x34), aliases: new[]{"EarFun Air 2 NC"}),
        new("TW205", "EarFun Air Life", "ef", "tw205", "ef_tw205", "battery version game eq keys find autooff reset", null, null, null, AO7, (0xF2,0x05)),
        new("TW230", "EarFun Air Pro 4i", "ef", "tw230", "ef_tw230", "battery version noise game eq keys dual prompt find quality autooff reset",
            N3, new[]{"DEFAULT","BALANCE","ADAPTIVE","WIND"}, new[]{"DEFAULT","NATURAL"}, AO6, (0x80,0x33), combineEq: true,
            ops: EfOpsPlus("SPATIAL_MODE","AMBIENT_NORMAL","ANC_AMBIENT","NOT_SET")),
        new("OW100", "EarFun OpenJump", "ef", "ef", "ef_ow100", EFF, null, null, null, AO7, (0x70,0x36)),
        new("OW100_NEW", "EarFun OpenJump (New)", "ef", "ef", "ef_ow100", EFF, null, null, null, AO7, (0x70,0x37), aliases: new[]{"EarFun OpenJump"}),
        new("CW100", "EarFun Clip", "ef", "cw100", "ef_cw100", "battery version game eq keys dual prompt find quality autooff reset",
            null, null, null, AO6, (0xF4,0x07), combineEq: true, ops: EfOpsPlus("BASS_EHANCEMENT","MANNER_MODE","NOT_SET")),
        new("CW200", "EarFun Clip 2", "ef", "cw200", "ef_cw200", "battery version game eq keys dual prompt find quality autooff reset",
            null, null, null, AO6, (0xC2,0x03), combineEq: true, ops: EfOpsPlus("BASS_EHANCEMENT","MANNER_MODE","NOT_SET")),
        new("HP201", "EarFun Wave Life", "ef", "head", "ef_hp201", "battery version noise game eq keys dual autooff reset",
            new[]{"normal","ambient","wind","anc2"}, null, null, AO6, (0x24,0x15), battery: "head"),
        new("HP220", "EarFun Tune Pro", "ef", "head", "ef_hp220", "battery version noise game eq keys dual autooff spatial reset",
            new[]{"normal","ambient","wind","anc2","anc1"}, null, null, AO6, (0xF3,0x06), battery: "head"),
        new("HP300", "EarFun Wave Pro 2", "ef", "head", "ef_hp220", "battery version noise game eq keys dual autooff spatial reset",
            new[]{"normal","ambient","wind","anc2","anc1"}, null, null, AO6, (0xC3,0x05), battery: "head"),
    };
    public static Model? ByKey(string? k) => MODELS.FirstOrDefault(m => m.Key == k);

    public static readonly Dictionary<string, string> UNSUPPORTED = new()
    {
        ["EarFun Air"] = "Airoha", ["EarFun Air Pro SV"] = "Airoha", ["EarFun Free 1S"] = "Bluetrum", ["EarFun Air 2"] = "Wuqi",
        ["EarFun Air Pro 2"] = "Wuqi", ["EarFun Wave Pro"] = "BES", ["EarFun UBOOM"] = "Speaker protocol", ["ATVEL Tune S5 Pro"] = "Wuqi",
        ["ATVEL Tune Pro Q7"] = "BES",
    };

    /// Guess the model from the Bluetooth name or firmware version, preferring the longest match.
    public static Model? GuessModel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.ToLowerInvariant();
        var tn = t.Replace("_", "").Replace("-", "");
        foreach (var m in MODELS.OrderByDescending(m => m.Key.Length))
        {
            var k = m.Key.ToLowerInvariant().Replace("_", "");
            if (k.Length >= 5 && tn.Contains(k) && !k.StartsWith("atvel")) return m;
        }
        Model? best = null; int bl = -1;
        foreach (var m in MODELS)
            foreach (var n in new[] { m.Name }.Concat(m.Aliases))
            {
                var n2 = n.ToLowerInvariant().Split(" (")[0];
                if (t.Contains(n2) && n2.Length > bl) { bl = n2.Length; best = m; }
            }
        return best;
    }

    public static string? UnsupportedChip(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var n = name.Trim().ToLowerInvariant();
        foreach (var kv in UNSUPPORTED) if (n == kv.Key.ToLowerInvariant()) return kv.Value;
        return null;
    }
}
