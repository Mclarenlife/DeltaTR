using System.Text;

namespace DeltaTR.Telemetry.ACC.Broadcast;

// ACC Broadcasting SDK v4. A datagram is parsed completely before it can change session state.
internal abstract record BroadcastPacket;
internal sealed record Registration(int Id, bool Success, bool ReadOnly, string Error) : BroadcastPacket;
internal sealed record Entries(int Id, ushort[] Cars) : BroadcastPacket;
internal sealed record Driver(string First, string Last, string Short, byte Category, ushort Nation);
internal sealed record Entry(ushort Id, byte Model, string Team, int Number, byte Cup, byte Driver,
    ushort Nation, Driver[] Drivers) : BroadcastPacket;
internal sealed record Lap(int Time, ushort Car, ushort Driver, int[] Splits, bool Invalid, bool PersonalBestEligible, bool Out, bool In);
internal sealed record Session(ushort Event, ushort Index, byte Type, byte Phase, float Elapsed, float End,
    int Focus, string CameraSet, string Camera, string Hud, bool Replay, float ReplayTime, float ReplayLeft,
    float Clock, byte Air, byte Road, byte Clouds, byte Rain, byte Wet, Lap Best) : BroadcastPacket;
internal sealed record Car(ushort Id, ushort Driver, byte Drivers, int Gear, float X, float Y, float Yaw,
    byte Location, ushort Speed, ushort Position, ushort Cup, ushort TrackPosition, float Spline, ushort Laps,
    int Delta, Lap Best, Lap Last, Lap Current) : BroadcastPacket;
internal sealed record Track(int Connection, string Name, int Id, int Length, string[] Cameras, string[] HudPages) : BroadcastPacket;
internal sealed record RaceEvent(byte Kind, string Message, int Time, int CarId) : BroadcastPacket;

internal static class BroadcastProtocol
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
    internal static BroadcastPacket? Parse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        using var r = new BinaryReader(stream, Utf8);
        BroadcastPacket? packet = r.ReadByte() switch
        {
            1 => new Registration(r.ReadInt32(), r.ReadByte() != 0, r.ReadByte() == 0, String(r)),
            2 => ReadSession(r),
            3 => new Car(r.ReadUInt16(), r.ReadUInt16(), r.ReadByte(), r.ReadByte() - 2, Float(r), Float(r), Float(r),
                r.ReadByte(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt16(), Float(r), r.ReadUInt16(),
                r.ReadInt32(), ReadLap(r), ReadLap(r), ReadLap(r)),
            4 => ReadEntries(r),
            5 => ReadTrack(r),
            6 => ReadEntry(r),
            7 => new RaceEvent(r.ReadByte(), String(r), r.ReadInt32(), r.ReadInt32()),
            _ => null
        };
        return stream.Position == stream.Length ? packet : null;
    }

    private static Entries ReadEntries(BinaryReader r)
    {
        var connection = r.ReadInt32();
        var count = r.ReadUInt16();
        if (count > 1024) throw new InvalidDataException("参赛车辆数量无效");
        var ids = new ushort[count];
        for (var i = 0; i < count; i++) ids[i] = r.ReadUInt16();
        return new(connection, ids);
    }

    private static Entry ReadEntry(BinaryReader r)
    {
        var id = r.ReadUInt16(); var model = r.ReadByte(); var team = String(r);
        var number = r.ReadInt32(); var cup = r.ReadByte(); var driver = r.ReadByte(); var nation = r.ReadUInt16();
        var drivers = new Driver[r.ReadByte()];
        for (var i = 0; i < drivers.Length; i++)
            drivers[i] = new(String(r), String(r), String(r), r.ReadByte(), r.ReadUInt16());
        return new(id, model, team, number, cup, driver, nation, drivers);
    }

    private static Session ReadSession(BinaryReader r)
    {
        var ev = r.ReadUInt16(); var index = r.ReadUInt16(); var type = r.ReadByte(); var phase = r.ReadByte();
        var elapsed = Float(r); var end = Float(r); var focus = r.ReadInt32();
        var set = String(r); var camera = String(r); var hud = String(r);
        var replay = r.ReadByte() != 0;
        var replayTime = replay ? Float(r) : 0; var left = replay ? Float(r) : 0;
        return new(ev, index, type, phase, elapsed, end, focus, set, camera, hud, replay, replayTime, left,
            Float(r), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte(), ReadLap(r));
    }

    private static Track ReadTrack(BinaryReader r)
    {
        var connection = r.ReadInt32(); var name = String(r); var id = r.ReadInt32(); var length = r.ReadInt32();
        var sets = new string[r.ReadByte()];
        for (var i = 0; i < sets.Length; i++)
            sets[i] = String(r) + "（" + string.Join("、", Strings(r)) + "）";
        return new(connection, name, id, length, sets, Strings(r));
    }

    private static string[] Strings(BinaryReader r)
    {
        var strings = new string[r.ReadByte()];
        for (var i = 0; i < strings.Length; i++) strings[i] = String(r);
        return strings;
    }

    private static Lap ReadLap(BinaryReader r)
    {
        var time = r.ReadInt32(); var car = r.ReadUInt16(); var driver = r.ReadUInt16();
        var splits = new int[r.ReadByte()];
        for (var i = 0; i < splits.Length; i++) splits[i] = r.ReadInt32();
        return new(time, car, driver, splits, r.ReadByte() != 0, r.ReadByte() != 0, r.ReadByte() != 0, r.ReadByte() != 0);
    }

    private static float Float(BinaryReader r)
    {
        var value = r.ReadSingle();
        return float.IsFinite(value) ? value : throw new InvalidDataException("非有限广播读数");
    }

    private static string String(BinaryReader r)
    {
        var length = r.ReadUInt16(); var bytes = r.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Utf8.GetString(bytes);
    }

    internal static byte[] Register(string password)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Utf8, true);
        w.Write((byte)1); w.Write((byte)4);
        WriteString(w, "DeltaTR"); WriteString(w, password); w.Write(250); WriteString(w, "");
        return stream.ToArray();
    }

    internal static byte[] Request(byte type, int connection)
    {
        if (type is not (9 or 10 or 11)) throw new ArgumentOutOfRangeException(nameof(type));
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(type); w.Write(connection);
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter w, string value)
    {
        var bytes = Utf8.GetBytes(value);
        w.Write(checked((ushort)bytes.Length)); w.Write(bytes);
    }
}
