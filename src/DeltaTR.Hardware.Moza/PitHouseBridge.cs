using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeltaTR.Hardware.Moza;

public sealed record BridgeSettings(int Port, string Key)
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeltaTR", "MozaBridge");
    public static BridgeSettings LoadOrCreate()
    {
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, "connection.json");
        if (File.Exists(path))
        {
            var value = JsonSerializer.Deserialize<BridgeSettings>(File.ReadAllText(path));
            if (value is not null && value.Port is >= 1024 and <= 65535 && value.Key.Length == 64 && value.Key.All(Uri.IsHexDigit)) return value;
            throw new IOException("屏显桥接配置损坏，请先恢复桥接后重新安装。");
        }
        var result = new BridgeSettings(17895, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(file, result);
        return result;
    }
}

/// <summary>Loopback-only, authenticated, bounded HTTP reader for the Pit House QML client.
/// Protocol 4 serves a dedicated field client with reversible dashboard switching.
/// Acknowledgement means the QML method returned, never proof of physical rendering.</summary>
public sealed class PitHouseBridge : IAsyncDisposable
{
    private readonly object sync = new();
    private readonly Func<bool> installationReady;
    private readonly Func<DateTimeOffset> now;
    private TcpListener? listener;
    private CancellationTokenSource? stop;
    private Task? worker;
    private BridgeSettings? settings;
    private WheelMessage message = new("idle", "");
    private DateTimeOffset updated, pageStart, lastPoll, lastAck;
    private bool enabled, outdatedClient;
    private string[] encoded = [];
    private string session = "";
    private string? failure;
    private long sequence, acknowledged;
    private string screenMode = "keep", screenStatus = "waiting";
    private long screenRequest = 1, screenTurn;
    private bool? radioVisible;
    public bool? IsRadioVisible { get { lock (sync) return radioVisible; } }
    public string ScreenStatus { get { lock (sync) return screenStatus; } }
    public void Update(WheelMessage value, string mode, long turn)
    {
        if (mode is not ("keep" or "radio" or "return")) throw new ArgumentException("Unknown screen mode");
        lock (sync)
        {
            Update(value);
            if (screenMode != mode || screenTurn != turn) { screenMode = mode; screenTurn = turn; screenRequest++; }
        }
    }
    public bool IsRunning => worker is { IsCompleted: false };
    public bool HasFailed => Volatile.Read(ref failure) is not null;
    public int Port => (listener?.LocalEndpoint as IPEndPoint)?.Port ?? 0;
    public PitHouseBridge() : this(() => PitHouseBridgeInstaller.IsInstalled, () => DateTimeOffset.UtcNow) { }
    internal PitHouseBridge(Func<bool> installationReady, Func<DateTimeOffset> clock)
    { this.installationReady = installationReady; now = clock; }
    public string Status
    {
        get
        {
            lock (sync)
            {
                if (failure is not null) return "桥接连接失败：" + failure;
                if (!IsRunning) return "Pit House 共存屏显已关闭";
                if (outdatedClient) return "Pit House 仍加载旧版桥接 · 请安装新版桥接并重启 Pit House";
                if (now() - lastPoll > TimeSpan.FromSeconds(3)) return "等待 Pit House 桥接 · 请安装桥接并重启 Pit House";
                if (now() - lastAck < TimeSpan.FromSeconds(3) && acknowledged > 0) return "Pit House 已接收屏显消息 · " + (screenStatus switch
                {
                    "radio" => "DeltaTR 画面", "race" => "比赛仪表", "switching" => "正在切换仪表",
                    "no-return" => "请先在 Pit House 选择比赛仪表，以便记录返回位置",
                    "dashboard" => "请上传 Fullscreen Radio 仪表", "device" => "等待 KS Pro 连接",
                    "timeout" => "仪表切换未确认，请重试", "external" => "已尊重手动切换",
                    "missing-return" => "原仪表已移除，请在 Pit House 重新选择", "busy" => "设备忙，等待切换",
                    "unavailable" => "仪表切换接口不可用", _ => "专用字段在线"
                });
                return "Pit House 桥接在线 · 等待消息提交确认";
            }
        }
    }
    public void Update(WheelMessage value)
    {
        lock (sync)
        {
            if (value.State != message.State || value.Turn != message.Turn) pageStart = now();
            message = value; updated = now();
        }
    }
    public void Start(BridgeSettings value)
    {
        if (IsRunning) throw new InvalidOperationException("屏显桥接已启动。");
        if (!installationReady()) throw new InvalidOperationException("请安装或升级专用字段桥接，重启 Pit House，并应用 DeltaTR KS Pro Fullscreen Radio 仪表。");
        if (value.Key.Length != 64 || !value.Key.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid bridge key");
        settings = value; failure = null;
        var socket = new TcpListener(IPAddress.Loopback, value.Port) { ExclusiveAddressUse = true };
        socket.Start(8);
        listener = socket;
        var cancellation = stop = new();
        lock (sync)
        {
            session = Guid.NewGuid().ToString("N"); enabled = true; encoded = []; sequence = acknowledged = 0;
            lastPoll = lastAck = default; updated = pageStart = now(); outdatedClient = false; radioVisible = null; screenStatus = "waiting";
        }
        worker = RunAsync(socket, cancellation.Token);
    }
    private async Task RunAsync(TcpListener socket, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var client = await socket.AcceptTcpClientAsync(token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(1500);
                try { await HandleAsync(client, timeout.Token); }
                catch (Exception e) when (e is IOException or SocketException or OperationCanceledException) { }
            }
        }
        catch (Exception e) when (token.IsCancellationRequested && e is OperationCanceledException or SocketException or ObjectDisposedException) { }
        catch (Exception e) { failure = e.Message; }
    }
    private async Task HandleAsync(TcpClient client, CancellationToken token)
    {
        var stream = client.GetStream();
        var bytes = new byte[4096]; int count = 0;
        while (count < bytes.Length)
        {
            int read = await stream.ReadAsync(bytes.AsMemory(count, 1), token);
            if (read == 0) return;
            count += read;
            if (count >= 4 && bytes.AsSpan(count - 4, 4).SequenceEqual("\r\n\r\n"u8)) break;
        }
        if (count == bytes.Length) { await ReplyAsync(stream, 431, "{}", token); return; }
        string[] lines = Encoding.ASCII.GetString(bytes, 0, count).Split("\r\n");
        var request = lines[0].Split(' ');
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1).Where(l => l.Length != 0))
        {
            int colon = line.IndexOf(':');
            if (colon < 1 || !headers.TryAdd(line[..colon], line[(colon + 1)..].Trim()))
            { await ReplyAsync(stream, 400, "{}", token); return; }
        }
        if (request.Length != 3 || request[0] != "GET" || request[2] != "HTTP/1.1" ||
            !request[1].StartsWith("/v1/screen?", StringComparison.Ordinal))
        { await ReplyAsync(stream, 404, "{}", token); return; }
        if (headers.GetValueOrDefault("Host") != $"127.0.0.1:{Port}" || headers.ContainsKey("Origin") ||
            !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(headers.GetValueOrDefault("X-DeltaTR-Key", "")), Encoding.ASCII.GetBytes(settings!.Key)))
        { await ReplyAsync(stream, 403, "{}", token); return; }
        var query = request[1][11..].Split('&').Select(s => s.Split('=', 2)).Where(p => p.Length == 2).ToArray();
        if (query.Count(p => p[0] == "protocol") != 1 || query.First(p => p[0] == "protocol")[1] != "4" ||
            query.Count(p => p[0] == "field") != 1 || query.First(p => p[0] == "field")[1] != "DeltaTRRadio")
        {
            lock (sync) outdatedClient = true;
            await ReplyAsync(stream, 409, "{}", token); return;
        }
        string clientSession = query.FirstOrDefault(p => p[0] == "session")?.ElementAt(1) ?? "";
        long.TryParse(query.FirstOrDefault(p => p[0] == "ack")?.ElementAt(1), out long ack);
        string json;
        lock (sync)
        {
            var time = now(); lastPoll = time; outdatedClient = false;
            screenStatus = query.FirstOrDefault(p => p[0] == "screen")?.ElementAt(1) ?? "waiting";
            string? display = query.FirstOrDefault(p => p[0] == "display")?.ElementAt(1);
            radioVisible = display == "1" ? true : display == "0" ? false : null;
            if (clientSession == session && ack > 0 && ack <= sequence && (ack > acknowledged || ack == sequence))
            { acknowledged = ack; lastAck = time; }
            bool fresh = time - updated < TimeSpan.FromSeconds(3);
            bool active = enabled && fresh;
            var rows = WheelScreen.EncodeRows(active ? message : new("idle", ""), time - pageStart);
            if (!rows.SequenceEqual(encoded)) { encoded = rows; sequence++; }
            json = JsonSerializer.Serialize(new { version = 4, field = PitHouseBridgeInstaller.Field, session, seq = sequence, enabled = active, screenMode, screenRequest,
                allowReset = true, rows, expires = time.AddSeconds(2).ToUnixTimeMilliseconds() });
        }
        await ReplyAsync(stream, 200, json, token);
    }
    private static async Task ReplyAsync(NetworkStream stream, int code, string json, CancellationToken token)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {code} {(code == 200 ? "OK" : "Error")}\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\nConnection: close\r\nContent-Length: {body.Length}\r\n\r\n");
        await stream.WriteAsync(header, token); await stream.WriteAsync(body, token);
    }
    public async Task StopAsync()
    {
        if (worker is null) return;
        lock (sync) enabled = false;
        // Give the polling client an explicit stop; its independent lease handles app crashes.
        await Task.Delay(600);
        stop!.Cancel(); listener!.Stop();
        try { await worker; } finally { stop.Dispose(); stop = null; listener = null; worker = null; }
    }
    public async ValueTask DisposeAsync() => await StopAsync();
}
