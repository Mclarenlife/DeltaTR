using System.IO.MemoryMappedFiles;
using DeltaTR.Core;
using DeltaTR.Telemetry.Abstractions;

namespace DeltaTR.Telemetry.Simulators;

public abstract class MappedGameAdapter(RacingGame game, string mapName) : IGameAdapter
{
    private MemoryMappedFile? file;
    private MemoryMappedViewAccessor? view;
    private DateTimeOffset retry, lastLive;
    private readonly FrameClock clock = new();
    private string session = "", identity = "";
    protected string Session => session;
    protected abstract RaceSnapshot Decode(MemoryMappedViewAccessor view, DateTimeOffset now);

    public RaceSnapshot Read()
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            if (view is null)
            {
                if (now < retry) return Empty(now, SessionPhase.Disconnected, $"等待 {game.Display()} 共享遥测");
                file = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.Read);
                view = file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                lastLive = now;
            }
            var frame = Decode(view, now);
            if (frame.Phase == SessionPhase.Live) lastLive = now;
            else if (now - lastLive > TimeSpan.FromSeconds(3)) Dispose();
            return frame;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentOutOfRangeException)
        {
            Dispose(); retry = now.AddSeconds(1);
            return Empty(now, SessionPhase.Disconnected, e switch
            {
                FileNotFoundException => $"等待 {game.Display()}，请启动游戏并进入驾驶会话。",
                UnauthorizedAccessException => "共享遥测读取权限不足，请以相同 Windows 用户运行。",
                _ => $"遥测暂不可用，将自动重试。{e.Message}"
            });
        }
    }

    protected DateTimeOffset? Advance(string key, double tick, DateTimeOffset now)
    {
        if (identity != key || tick < clock.Tick)
        {
            identity = key; session = Guid.NewGuid().ToString("N"); clock.Reset(); OnSessionChanged();
        }
        var observed = clock.Observe(tick, now);
        if (now - clock.LastChange > Metrics.MaxAge) throw new IOException("游戏数据已停止更新");
        return observed;
    }

    protected virtual void OnMappingClosed() { }
    protected virtual void OnSessionChanged() { }
    protected RaceSnapshot Empty(DateTimeOffset now, SessionPhase phase, string status) =>
        RaceSnapshot.Empty(now, 0, phase, status) with { Game = game, SessionId = session };

    protected RaceSnapshot Live(DateTimeOffset now, string car, string track, int tick, Dictionary<string, Measurement> fields) =>
        new RaceSnapshot(1, session, now, 0, SessionPhase.Live, DataOrigin.Live, "", "", car, track, tick, null,
            $"{game.Display()} · 已收到驾驶遥测", fields) { Game = game };

    protected static void Add(Dictionary<string, Measurement> fields, string key, double? value, string unit,
        DateTimeOffset observed, string source, double min = double.MinValue, double max = double.MaxValue)
    {
        if (value is double n && double.IsFinite(n) && n >= min && n <= max)
            fields[key] = new(n, unit, observed, DataQuality.Valid, source, "");
    }

    public void Dispose()
    {
        view?.Dispose(); file?.Dispose(); view = null; file = null;
        clock.Reset(); identity = ""; session = ""; OnSessionChanged(); OnMappingClosed();
    }
}

internal sealed class FrameClock
{
    public double Tick { get; private set; } = double.NaN;
    public DateTimeOffset LastChange { get; private set; }
    private bool advancing;
    public DateTimeOffset? Observe(double tick, DateTimeOffset now)
    {
        if (!double.IsFinite(tick)) return null;
        if (tick != Tick)
        {
            advancing = !double.IsNaN(Tick) && tick > Tick;
            Tick = tick; LastChange = now;
        }
        return advancing && now - LastChange <= Metrics.MaxAge ? LastChange : null;
    }
    public void Reset() { Tick = double.NaN; advancing = false; LastChange = default; }
}
