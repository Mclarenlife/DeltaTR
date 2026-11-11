using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeltaTR.Core;

public enum DataQuality { Valid, Estimated, Stale, Unavailable, Invalid, NeedsVerification }
public enum SessionPhase { Disconnected, Connecting, Idle, Live, Paused, Replay, Faulted }
public enum DataOrigin { Live, Synthetic }

public sealed record Measurement(
    double? Value, string Unit, DateTimeOffset ObservedAt, DataQuality Quality,
    string Source, string GameVersion, bool IsEstimated = false)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TextValue { get; init; }
    [JsonIgnore] public string? ImperialText { get; init; }

    public bool IsUsable(DateTimeOffset now, TimeSpan maxAge) =>
        (Value is { } v && double.IsFinite(v) || !string.IsNullOrWhiteSpace(TextValue)) && Quality == DataQuality.Valid &&
        now >= ObservedAt && now - ObservedAt <= maxAge;
}

// The penalty is the current ACC penalty code, LMU outstanding count, or iRacing black flag level.
public sealed record RaceControlState(bool? Yellow, int? Penalty, DateTimeOffset ObservedAt)
{
    public bool? Blue { get; init; }
}

public sealed record RaceSnapshot(
    int SchemaVersion, string SessionId, DateTimeOffset CapturedAt, long ElapsedMilliseconds,
    SessionPhase Phase, DataOrigin Origin, string GameVersion, string SharedMemoryVersion,
    string CarModel, string Track, int? PhysicsPacketId, int? GraphicsPacketId,
    string StatusMessage, Dictionary<string, Measurement> Fields)
{
    // Broadcast rosters contain real names. Keep them available to the local radio only.
    [JsonIgnore] public IReadOnlyDictionary<string, Measurement> LocalFields { get; init; } = new Dictionary<string, Measurement>();
    [JsonIgnore] public RaceTraffic? Traffic { get; init; }
    [JsonIgnore] public RaceControlState? RaceControl { get; init; }
    [JsonIgnore] public NativeTelemetry? Native { get; init; }
    [JsonIgnore] public BroadcastFrame? Broadcast { get; init; }
    public Measurement? Reading(string key) => key.StartsWith("UDP.", StringComparison.Ordinal)
        ? Broadcast?.Fields.GetValueOrDefault(key) : Fields.GetValueOrDefault(key) ?? LocalFields.GetValueOrDefault(key) ?? Native?.Read(key);

    public RacingGame Game { get; init; } = RacingGame.ACC;
    public const int CurrentSchema = 1;
    public static RaceSnapshot Empty(DateTimeOffset now, long elapsed, SessionPhase phase, string message) =>
        new(CurrentSchema, "", now, elapsed, phase, DataOrigin.Live, "", "", "", "", null, null, message, []);
}

public static class TelemetryJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };
}

public static class Metrics
{
    public static readonly string[] Wheels = ["FL", "FR", "RL", "RR"];
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(2);
}
