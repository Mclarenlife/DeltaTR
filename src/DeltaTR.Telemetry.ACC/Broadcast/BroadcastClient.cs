using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using DeltaTR.Core;

namespace DeltaTR.Telemetry.ACC.Broadcast;

// Driven by the adapter's single sampling thread. No socket reads block the physics feed.
internal sealed class BroadcastClient(Func<(int Port, string Password)> config) : IDisposable
{
    private UdpClient? socket;
    private BroadcastState state = new();
    private int? connection;
    private DateTimeOffset retry, lastHeartbeat, lastRequest, nextSnapshot;
    private BroadcastFrame result = BroadcastFrame.Empty("等待 ACC 广播");
    private int? game;
    private string? sharedSession;

    public BroadcastFrame Poll(int? process, RaceSnapshot shared, DateTimeOffset now)
    {
        if (game != process) { Reset(); game = process; retry = now; }
        if (process is null) return BroadcastFrame.Empty("广播等待 ACC 启动");
        if (shared.Phase == SessionPhase.Live && sharedSession != shared.SessionId)
        {
            if (sharedSession is not null) { Reset(); retry = now; }
            sharedSession = shared.SessionId;
        }
        try
        {
            if (socket is null)
            {
                if (now < retry) return result;
                retry = now.AddSeconds(4);
                var (port, password) = config();
                if (port is < 1 or > 65535) return result = BroadcastFrame.Empty("ACC 广播未开启，请在下方启用");
                socket = new UdpClient(AddressFamily.InterNetwork);
                socket.Connect(IPAddress.Loopback, port);
                var registration = BroadcastProtocol.Register(password);
                socket.Send(registration, registration.Length);
                lastHeartbeat = now;
                result = BroadcastFrame.Empty("正在注册 ACC 只读广播");
            }
            for (var i = 0; i < 128 && socket.Available > 0; i++)
            {
                IPEndPoint? endpoint = null;
                var bytes = socket.Receive(ref endpoint);
                BroadcastPacket? packet;
                try { packet = BroadcastProtocol.Parse(bytes); }
                catch (Exception e) when (e is IOException or DecoderFallbackException or InvalidDataException) { continue; }
                if (packet is Registration registration && connection is null)
                {
                    if (!registration.Success)
                    {
                        Reset(); retry = now.AddSeconds(5);
                        return result = BroadcastFrame.Empty("ACC 拒绝注册，请核对游戏广播配置并重新检测");
                    }
                    connection = registration.Id;
                    RequestMetadata(now); lastHeartbeat = now;
                }
                else if (connection is int id && packet is not null)
                {
                    if (packet is Session) lastHeartbeat = now;
                    if (state.Apply(packet, id, now)) RequestMetadata(now);
                }
            }
            if (now - lastHeartbeat > TimeSpan.FromSeconds(4))
            {
                Reset(); retry = now.AddSeconds(1);
                return result = BroadcastFrame.Empty("ACC 广播超时，将自动重连");
            }
            if (connection is not null)
            {
                var interval = state.NeedsEntries || state.NeedsTrack ? 2 : 30;
                if (now - lastRequest >= TimeSpan.FromSeconds(interval)) RequestMetadata(now);
                if (now >= nextSnapshot)
                {
                    nextSnapshot = now.AddMilliseconds(200);
                    result = state.Snapshot(shared, now);
                }
            }
        }
        catch (Exception e) when (e is IOException or SocketException or UnauthorizedAccessException or
            InvalidDataException or JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            Reset(); retry = now.AddSeconds(4);
            result = BroadcastFrame.Empty(e is FileNotFoundException or DirectoryNotFoundException
                ? "找不到 ACC 广播配置，请先启动一次游戏" : "广播读取失败，将自动重试，请检查 ACC 广播配置");
        }
        return result;
    }

    private void RequestMetadata(DateTimeOffset now)
    {
        Send(10); Send(11); lastRequest = now;
    }
    private void Send(byte type)
    {
        if (socket is null || connection is not int id) return;
        var bytes = BroadcastProtocol.Request(type, id);
        socket.Send(bytes, bytes.Length);
    }
    private void Reset()
    {
        try { Send(9); } catch (SocketException) { }
        socket?.Dispose(); socket = null; connection = null; state = new(); sharedSession = null;
        nextSnapshot = default; result = BroadcastFrame.Empty("等待 ACC 广播");
    }
    public void Dispose() => Reset();
}
