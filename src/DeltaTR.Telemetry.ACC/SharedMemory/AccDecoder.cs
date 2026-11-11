using System.Buffers.Binary;
using System.Text;

namespace DeltaTR.Telemetry.ACC.SharedMemory;

public sealed record RawValue(double Value, string SuggestedUnit, string Source, double Min, double Max, string? TextValue = null);
public sealed record AccRawFrame(int PhysicsPacketId, int GraphicsPacketId, int Status, int SessionType,
    int SessionIndex, int CompletedLaps, string GameVersion, string SharedMemoryVersion,
    string CarModel, string Track, Dictionary<string, RawValue> Values)
{
    [System.Text.Json.Serialization.JsonIgnore] public Dictionary<string, RawValue> LocalFields { get; init; } = [];
}

public static class AccDecoder
{
    public static AccRawFrame Decode(byte[] physics, byte[] graphics, byte[] info)
    {
        var p = AccLayout.Physics;
        var g = AccLayout.Graphics;
        var s = AccLayout.Static;
        if (physics.Length < p.Size || graphics.Length < g.Size || info.Length < s.Size)
            throw new InvalidDataException("ACC 内存页过短，无法按 1.8.12 布局解析。");
        var values = new Dictionary<string, RawValue>();
        void PF(string id, string field, string unit, double min, double max, int index = 0) =>
            values.Add(id, new(Float(physics, p[field] + index * 4), unit, $"ACC.Physics.{field}" + (field.StartsWith("wheel") || field.StartsWith("tyre") ? $"[{index}]" : ""), min, max));
        void GI(string id, string field, string unit, double min, double max) =>
            values.Add(id, new(Int(graphics, g[field]), unit, $"ACC.Graphics.{field}", min, max));
        void GF(string id, string field, string unit, double min, double max) =>
            values.Add(id, new(Float(graphics, g[field]), unit, $"ACC.Graphics.{field}", min, max));
        PF("speed", "speedKmh", "km/h", 0, 500);
        PF("fuel", "fuel", "raw", 0, 1000);
        PF("airTemperature", "airTemp", "°C", -40, 80);
        PF("roadTemperature", "roadTemp", "°C", -40, 100);
        values.Add("gear", new(Int(physics, p["gear"]), "ACC gear code", "ACC.Physics.gear", 0, 10));
        values.Add("rpm", new(Int(physics, p["rpm"]), "rpm", "ACC.Physics.rpm", 0, 25000));
        for (var i = 0; i < 4; i++)
        {
            PF($"pressure.{Core.Metrics.Wheels[i]}", "wheelPressure", "psi", 0.1, 80, i);
            PF($"temperature.{Core.Metrics.Wheels[i]}", "tyreCoreTemperature", "°C", -40, 250, i);
        }
        GI("position", "position", "position", 1, 1000);
        GI("completedLaps", "completedLaps", "lap", 0, 100000);
        GI("lastLapTime", "iLastTime", "ms", 1, 3600000);
        GI("gapAhead", "gapAhead", "ms", 1, 3600000);
        GI("gapBehind", "gapBehind", "ms", 1, 3600000);
        GI("rainIntensity", "rainIntensity", "category", 0, 5);
        GI("rainIn10Minutes", "rainIntensityIn10min", "category", 0, 5);
        GI("rainIn30Minutes", "rainIntensityIn30min", "category", 0, 5);
        GI("trackGrip", "trackGripStatus", "category", 0, 6);
        GI("missingMandatoryPits", "missingMandatoryPits", "count", 0, 100);
        GI("rainTyres", "rainTyres", "boolean", 0, 1);
        GI("tyreSet", "currentTyreSet", "set code", 0, 100);
        GI("inPitLane", "isInPitLane", "boolean", 0, 1);
        GI("validLap", "isValidLap", "boolean", 0, 1);
        GF("sessionTimeLeft", "sessionTimeLeft", "raw", 0, 604800000);
        GF("gameFuelPerLap", "fuelXLap", "L/lap", 0.01, 100);
        GF("gameFuelEstimatedLaps", "fuelEstimatedLaps", "lap", 0, 10000);
        var local = new Dictionary<string, RawValue>();
        ExtendedAccReader.Read(physics, graphics, info, values, local);
        return new(Int(physics, 0), Int(graphics, 0), Int(graphics, g["status"]),
            Int(graphics, g["session"]), Int(graphics, g["sessionIndex"]), Int(graphics, g["completedLaps"]),
            Text(info, s["acVersion"], 15), Text(info, s["smVersion"], 15),
            Text(info, s["carModel"], 33), Text(info, s["track"], 33), values) { LocalFields = local };
    }

    private static int Int(byte[] b, int offset) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(offset, 4));
    private static float Float(byte[] b, int offset) => BitConverter.Int32BitsToSingle(Int(b, offset));
    private static string Text(byte[] b, int offset, int chars) => Encoding.Unicode.GetString(b, offset, chars * 2).Split('\0')[0];
}
