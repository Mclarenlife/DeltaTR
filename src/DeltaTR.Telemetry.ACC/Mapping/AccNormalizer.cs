using DeltaTR.Core;
using DeltaTR.Telemetry.ACC.SharedMemory;

namespace DeltaTR.Telemetry.ACC.Mapping;

public sealed class AccNormalizer(CalibrationProfile calibration)
{
    private string sessionId = "";
    private string identity = "";
    private int? lastPhysics, lastGraphics;
    private int lastLaps;
    private SessionPhase previousPhase = SessionPhase.Disconnected;
    private bool physicsUpdating, graphicsUpdating;
    private readonly SessionClockUnits sessionClock = new();
    private long physicsChanged, graphicsChanged;
    private DateTimeOffset physicsObserved, graphicsObserved;

    public void Reset()
    {
        sessionId = identity = "";
        lastPhysics = lastGraphics = null;
        previousPhase = SessionPhase.Disconnected;
        physicsUpdating = graphicsUpdating = false;
    }

    public RaceSnapshot Normalize(AccRawFrame raw, DateTimeOffset now, long elapsed)
    {
        var phase = raw.Status switch
        {
            0 => SessionPhase.Idle, 1 => SessionPhase.Replay, 2 => SessionPhase.Live,
            3 => SessionPhase.Paused, _ => SessionPhase.Faulted
        };
        if (string.IsNullOrWhiteSpace(raw.GameVersion) || string.IsNullOrWhiteSpace(raw.CarModel) ||
            string.IsNullOrWhiteSpace(raw.Track) || string.IsNullOrWhiteSpace(raw.SharedMemoryVersion))
            phase = SessionPhase.Connecting;
        var newIdentity = $"{raw.GameVersion}|{raw.SharedMemoryVersion}|{raw.CarModel}|{raw.Track}|{raw.SessionType}|{raw.SessionIndex}";
        var boundary = identity != newIdentity || raw.CompletedLaps < lastLaps ||
            (previousPhase is SessionPhase.Idle or SessionPhase.Replay or SessionPhase.Disconnected && phase == SessionPhase.Live) ||
            (lastPhysics.HasValue && raw.PhysicsPacketId < lastPhysics && lastPhysics - (long)raw.PhysicsPacketId < int.MaxValue) ||
            (lastGraphics.HasValue && raw.GraphicsPacketId < lastGraphics && lastGraphics - (long)raw.GraphicsPacketId < int.MaxValue);
        if (boundary)
        {
            sessionId = Guid.NewGuid().ToString("N");
            lastPhysics = lastGraphics = null;
        }
        // Require a second live packet from the relevant page, even after resuming a paused session.
        if (boundary || phase != SessionPhase.Live || previousPhase != SessionPhase.Live)
            physicsUpdating = graphicsUpdating = false;
        else
        {
            physicsUpdating |= lastPhysics != raw.PhysicsPacketId;
            graphicsUpdating |= lastGraphics != raw.GraphicsPacketId;
        }
        if (boundary || phase != SessionPhase.Live || previousPhase != SessionPhase.Live) sessionClock.Reset();
        if (phase == SessionPhase.Live && lastGraphics != raw.GraphicsPacketId && raw.Values.TryGetValue("sessionTimeLeft", out var clock))
            sessionClock.Observe(clock.Value, elapsed);
        identity = newIdentity;
        lastLaps = raw.CompletedLaps;
        previousPhase = phase;
        if (lastPhysics != raw.PhysicsPacketId) { physicsChanged = elapsed; physicsObserved = now; lastPhysics = raw.PhysicsPacketId; }
        if (lastGraphics != raw.GraphicsPacketId) { graphicsChanged = elapsed; graphicsObserved = now; lastGraphics = raw.GraphicsPacketId; }
        var matches = calibration.Matches(raw.GameVersion, raw.SharedMemoryVersion, raw.CarModel, raw.Track);
        var supportedLayout = raw.SharedMemoryVersion is "1.8.12" or "1.9";
        var fields = new Dictionary<string, Measurement>();
        var local = new Dictionary<string, Measurement>();
        foreach (var (key, value) in raw.Values.Concat(raw.LocalFields))
        {
            var physics = value.Source.StartsWith("ACC.Physics.", StringComparison.Ordinal);
            var observed = physics ? physicsObserved : graphicsObserved;
            var age = elapsed - (physics ? physicsChanged : graphicsChanged);
            var finite = double.IsFinite(value.Value);
            var quality = value.TextValue is "" ? DataQuality.Unavailable : !finite || value.Value < value.Min || value.Value > value.Max ? DataQuality.Invalid :
                phase != SessionPhase.Live ? DataQuality.Unavailable :
                age > Metrics.MaxAge.TotalMilliseconds ? DataQuality.Stale : DataQuality.NeedsVerification;
            var unit = value.SuggestedUnit;
            if (quality == DataQuality.NeedsVerification && matches && calibration.VerifiedUnits.TryGetValue(key, out var verifiedUnit)
                && AcceptUnit(key, value.SuggestedUnit, verifiedUnit))
            {
                unit = verifiedUnit;
                quality = DataQuality.Valid;
            }
            if (quality == DataQuality.NeedsVerification && supportedLayout)
            {
                unit = key switch
                {
                    "fuel" => "L",
                    "sessionTimeLeft" => sessionClock.Unit ?? "raw",
                    _ => unit
                };
                quality = unit != "raw" && (physics ? physicsUpdating : graphicsUpdating)
                    ? DataQuality.Valid : DataQuality.Unavailable;
            }
            var estimated = key is "gameFuelEstimatedLaps" or "gameFuelPerLap";
            if (estimated && quality == DataQuality.Valid) quality = DataQuality.Estimated;
            var destination = raw.LocalFields.ContainsKey(key) ? local : fields;
            destination[key] = new(finite && value.TextValue is null ? value.Value : null, unit, observed, quality, value.Source, raw.GameVersion, estimated)
                { TextValue = value.TextValue };
        }
        int? ControlValue(string name) => fields.TryGetValue("Graphics." + name, out var reading) &&
            reading.IsUsable(now, Metrics.MaxAge) && reading.Value is double value ? (int)value : null;
        var flag = ControlValue("flag");
        bool? yellow = flag.HasValue ? flag == 2 : null;
        if (ControlValue("globalYellow") == 1 || ControlValue("globalYellow1") == 1 ||
            ControlValue("globalYellow2") == 1 || ControlValue("globalYellow3") == 1) yellow = true;

        bool? racing = null;
        bool? finished = null;
        if (supportedLayout && graphicsUpdating && phase == SessionPhase.Live)
        {
            racing = raw.SessionType == 2 && (ControlValue("globalGreen") == 1 || yellow == true);
            finished = raw.SessionType == 2 && ControlValue("globalChequered") == 1;
        }
        var traffic = new RaceTraffic(ControlValue("playerCarID") ?? -1, [], graphicsObserved, racing, finished);
        var message = phase switch
        {
            SessionPhase.Live when supportedLayout => "遥测已连接 · 排名、圈数等基础数据随有效数据包自动启用",
            SessionPhase.Live => matches ? "遥测已连接 · 使用已核验的字段配置" : "遥测已连接 · 当前共享内存版本尚无内置字段规则",
            SessionPhase.Paused => "游戏暂停 · 禁止实时问答",
            SessionPhase.Replay => "游戏重播 · 禁止实时问答",
            SessionPhase.Idle => "游戏未进入驾驶会话",
            SessionPhase.Connecting => "等待有效车辆、赛道与版本信息",
            _ => "未知 ACC 状态 · 请核对协议版本"
        };
        return new(1, sessionId, now, elapsed, phase, DataOrigin.Live, raw.GameVersion, raw.SharedMemoryVersion,
            raw.CarModel, raw.Track, raw.PhysicsPacketId, raw.GraphicsPacketId, message, fields) { LocalFields = local, Traffic = traffic, RaceControl = new(yellow, ControlValue("penalty"), graphicsObserved) { Blue = flag.HasValue ? flag == 1 : null } };
    }

    private static bool AcceptUnit(string key, string suggested, string verified) => key switch
    {
        "fuel" => verified is "L" or "kg",
        "sessionTimeLeft" => verified is "ms" or "s",
        _ => suggested == verified
    };
}
