using System.Collections.Concurrent;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;

namespace EarKit.Core;

public interface ILink : IDisposable
{
    string Desc { get; }
    void Write(byte[] b);
    /// 读取一批数据；超时返回空数组；连接断开时抛异常
    byte[] Read();
}

public static class Bt
{
    public static readonly Guid SPP = new("00001101-0000-1000-8000-00805F9B34FB");
}

// ------------------------------------------------------------ 蓝牙串口 COM
public sealed class SerialLink : ILink
{
    readonly SerialPort _p;
    public string Desc { get; }
    public SerialLink(string port)
    {
        _p = new SerialPort(port, 115200) { ReadTimeout = 100, WriteTimeout = 2000 };
        _p.Open();
        Desc = port;
    }
    public void Write(byte[] b) => _p.Write(b, 0, b.Length);
    public byte[] Read()
    {
        try
        {
            int n = _p.BytesToRead;
            var buf = new byte[Math.Max(n, 1)];
            int r = _p.Read(buf, 0, buf.Length);
            return buf[..r];
        }
        catch (TimeoutException) { return Array.Empty<byte>(); }
    }
    public void Dispose() { try { _p.Close(); } catch { } try { _p.Dispose(); } catch { } }
}

// ------------------------------------------------------------ Winsock AF_BTH RFCOMM
sealed class BthEndPoint : EndPoint
{
    const int AF_BTH = 32;
    readonly ulong _addr; readonly Guid _svc; readonly int _port;
    public BthEndPoint(ulong addr, Guid svc, int port) { _addr = addr; _svc = svc; _port = port; }
    public override AddressFamily AddressFamily => (AddressFamily)AF_BTH;
    public override SocketAddress Serialize()
    {
        var sa = new SocketAddress((AddressFamily)AF_BTH, 30);
        for (int i = 0; i < 8; i++) sa[2 + i] = (byte)((_addr >> (8 * i)) & 0xFF);
        var g = _svc.ToByteArray();
        for (int i = 0; i < 16; i++) sa[10 + i] = g[i];
        for (int i = 0; i < 4; i++) sa[26 + i] = (byte)((_port >> (8 * i)) & 0xFF);
        return sa;
    }
    public override EndPoint Create(SocketAddress sa) => this;
}

public sealed class BthSocketLink : ILink
{
    readonly Socket _s;
    public string Desc { get; }
    public BthSocketLink(ulong addr, int channel, int timeoutMs = 6000)
    {
        _s = new Socket((AddressFamily)32, SocketType.Stream, (ProtocolType)3);
        var ep = channel <= 0 ? new BthEndPoint(addr, Bt.SPP, 0) : new BthEndPoint(addr, Guid.Empty, channel);
        var t = Task.Run(() => _s.Connect(ep));
        t.ContinueWith(x => { _ = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        bool ok;
        try { ok = t.Wait(timeoutMs); }
        catch (AggregateException ae) { _s.Dispose(); throw ae.InnerException ?? ae; }
        if (!ok) { _s.Dispose(); throw new TimeoutException("连接超时"); }
        Desc = channel <= 0 ? $"{Util.FmtMac(addr)} (SPP)" : $"{Util.FmtMac(addr)} 通道 {channel}";
    }
    public void Write(byte[] b) => _s.Send(b);
    public byte[] Read()
    {
        // 先 select 等待（150ms），有数据再读，不依赖 SO_RCVTIMEO
        if (!_s.Poll(150_000, SelectMode.SelectRead)) return Array.Empty<byte>();
        var buf = new byte[1024];
        int n = _s.Receive(buf);
        if (n == 0) throw new IOException("对端关闭连接");
        return buf[..n];
    }
    public void Dispose() { try { _s.Shutdown(SocketShutdown.Both); } catch { } try { _s.Dispose(); } catch { } }
}

// ------------------------------------------------------------ WinRT RFCOMM
public sealed class WinRtLink : ILink
{
    readonly StreamSocket _sock;
    readonly Stream _in, _out;
    readonly BlockingCollection<byte[]> _q = new();
    volatile Exception? _err;
    volatile bool _closed;
    public string Desc { get; }

    WinRtLink(StreamSocket s, string desc)
    {
        _sock = s; Desc = desc;
        _in = s.InputStream.AsStreamForRead(0);
        _out = s.OutputStream.AsStreamForWrite(0);
        new Thread(Pump) { IsBackground = true, Name = "WinRtRx" }.Start();
    }
    void Pump()
    {
        var buf = new byte[1024];
        try
        {
            while (!_closed)
            {
                int n = _in.Read(buf, 0, buf.Length);
                if (n <= 0) throw new IOException("对端关闭连接");
                _q.Add(buf[..n]);
            }
        }
        catch (Exception e) { if (!_closed) _err = e; }
    }
    public static async Task<WinRtLink> ConnectAsync(RfcommDeviceService svc, string desc, int timeoutMs = 7000)
    {
        var s = new StreamSocket();
        var t = s.ConnectAsync(svc.ConnectionHostName, svc.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask();
        _ = t.ContinueWith(x => { _ = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        if (await Task.WhenAny(t, Task.Delay(timeoutMs)) != t) { s.Dispose(); throw new TimeoutException("连接超时"); }
        await t;
        return new WinRtLink(s, desc);
    }
    public void Write(byte[] b) { _out.Write(b, 0, b.Length); _out.Flush(); }
    public byte[] Read()
    {
        if (_q.TryTake(out var d, 120)) return d;
        if (_err != null) throw _err;
        return Array.Empty<byte>();
    }
    public void Dispose() { _closed = true; try { _sock.Dispose(); } catch { } }
}

// ------------------------------------------------------------ 设备发现
public sealed class DeviceEntry
{
    public string Name = "";
    public string? Mac;          // AA:BB:CC:DD:EE:FF
    public string? Com;          // COMx
    public string? WinRtId;
    public bool Connected;
    public Model? Guess => Catalog.GuessModel(Name);
    public bool IsEarfun => Name.Contains("earfun", StringComparison.OrdinalIgnoreCase) || Name.Contains("atvel", StringComparison.OrdinalIgnoreCase);
    public string Display
    {
        get
        {
            var n = string.IsNullOrEmpty(Name) ? "(未知名称)" : Name;
            if (Com != null) return $"{n}  ·  {Com}";
            return n + (Connected ? "  ·  已连接" : "");
        }
    }
    public override string ToString() => Display;
}

public static class Discovery
{
    static string FmtMac(string h) { h = h.ToUpperInvariant(); return string.Join(":", Enumerable.Range(0, 6).Select(i => h.Substring(i * 2, 2))); }

    /// 读取注册表中已配对的经典蓝牙设备：MAC -> 名称
    public static Dictionary<string, string> PairedFromRegistry()
    {
        var o = new Dictionary<string, string>();
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices");
            if (k == null) return o;
            foreach (var sub in k.GetSubKeyNames())
            {
                try
                {
                    using var dk = k.OpenSubKey(sub);
                    if (dk?.GetValue("Name") is byte[] raw && sub.Length == 12) o[FmtMac(sub)] = Util.CStr(raw);
                }
                catch { }
            }
        }
        catch { }
        return o;
    }

    /// 通过 WinRT 枚举已配对的经典蓝牙设备（含连接状态）
    public static async Task<List<DeviceEntry>> PairedFromWinRtAsync()
    {
        var res = new List<DeviceEntry>();
        try
        {
            var infos = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
            foreach (var di in infos)
            {
                try
                {
                    using var dev = await BluetoothDevice.FromIdAsync(di.Id);
                    if (dev == null) continue;
                    res.Add(new DeviceEntry
                    {
                        Name = string.IsNullOrEmpty(dev.Name) ? di.Name : dev.Name,
                        Mac = Util.FmtMac(dev.BluetoothAddress),
                        WinRtId = di.Id,
                        Connected = dev.ConnectionStatus == BluetoothConnectionStatus.Connected,
                    });
                }
                catch { }
            }
        }
        catch { }
        return res;
    }

    /// 蓝牙串口（传出）COM 口：(COMx, MAC)
    public static List<(string Port, string? Mac)> BtComPorts()
    {
        var res = new List<(string, string?)>();
        HashSet<string> present;
        try { present = new HashSet<string>(SerialPort.GetPortNames(), StringComparer.OrdinalIgnoreCase); } catch { present = new(); }
        try
        {
            using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\BTHENUM");
            if (root == null) return res;
            foreach (var svc in root.GetSubKeyNames())
            {
                using var sk = root.OpenSubKey(svc);
                if (sk == null) continue;
                foreach (var inst in sk.GetSubKeyNames())
                {
                    try
                    {
                        using var dp = sk.OpenSubKey(inst + @"\Device Parameters");
                        var port = dp?.GetValue("PortName") as string;
                        if (string.IsNullOrEmpty(port) || !present.Contains(port)) continue;
                        var m = Regex.Match(inst.ToUpperInvariant(), "&([0-9A-F]{12})_");
                        if (!m.Success || m.Groups[1].Value == "000000000000") continue;   // 传入口无法用来连接耳机
                        res.Add((port, FmtMac(m.Groups[1].Value)));
                    }
                    catch { }
                }
            }
        }
        catch { }
        return res.DistinctBy(x => x.Item1).OrderBy(x => x.Item1).ToList();
    }

    public static async Task<List<DeviceEntry>> ListAsync()
    {
        var reg = PairedFromRegistry();
        var wr = await PairedFromWinRtAsync();
        var list = new List<DeviceEntry>();
        foreach (var e in wr)
        {
            if (string.IsNullOrEmpty(e.Name) && e.Mac != null && reg.TryGetValue(e.Mac, out var n)) e.Name = n;
            list.Add(e);
        }
        foreach (var kv in reg)
            if (!list.Any(e => e.Mac == kv.Key)) list.Add(new DeviceEntry { Name = kv.Value, Mac = kv.Key });
        return list.OrderByDescending(e => e.IsEarfun).ThenByDescending(e => e.Connected).ThenBy(e => e.Name).ToList();
    }

    public static async Task<bool?> IsConnectedAsync(string mac)
    {
        try
        {
            using var dev = await BluetoothDevice.FromBluetoothAddressAsync(Util.ParseMac(mac));
            if (dev == null) return null;
            return dev.ConnectionStatus == BluetoothConnectionStatus.Connected;
        }
        catch { return null; }
    }

    /// 取设备的 RFCOMM 服务（SPP 优先，其余在后）
    public static async Task<List<RfcommDeviceService>> RfcommServicesAsync(string mac, Action<string> log)
    {
        var o = new List<RfcommDeviceService>();
        try
        {
            var dev = await BluetoothDevice.FromBluetoothAddressAsync(Util.ParseMac(mac));
            if (dev == null) { log("WinRT：找不到该蓝牙设备"); return o; }
            try { await dev.RequestAccessAsync(); } catch { }
            var spp = await dev.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Uncached);
            if (spp.Error == BluetoothError.Success) o.AddRange(spp.Services);
            else log($"WinRT：查询 SPP 服务失败 {spp.Error}");
            var all = await dev.GetRfcommServicesAsync(BluetoothCacheMode.Cached);
            if (all.Error == BluetoothError.Success)
                foreach (var s in all.Services)
                    if (!o.Any(x => x.ServiceId.Uuid == s.ServiceId.Uuid && x.ConnectionServiceName == s.ConnectionServiceName)) o.Add(s);
            log($"WinRT：发现 {o.Count} 个 RFCOMM 服务：" + string.Join(", ", o.Select(s => $"{s.ServiceId.Uuid}@{s.ConnectionServiceName}")));
        }
        catch (Exception e) { log("WinRT：" + e.Message); }
        return o;
    }
}
