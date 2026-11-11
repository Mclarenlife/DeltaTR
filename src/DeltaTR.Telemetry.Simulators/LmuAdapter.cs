using System.IO.MemoryMappedFiles;
using System.Text;
using DeltaTR.Core;

namespace DeltaTR.Telemetry.Simulators;

/// <summary>LMU_Data, Windows x64 / pack 4. See docs/GAME_TELEMETRY.md for the layout reference.</summary>
public sealed class LmuAdapter : MappedGameAdapter
{
    public LmuAdapter() : this("LMU_Data") { }
    internal LmuAdapter(string mapping) : base(RacingGame.LMU, mapping) { }
    internal const int Size = 324820;
    private const int Score = 1632, Vehicles = Score + 560, Telemetry = 128464;

    private readonly FrameClock scoringClock = new();
    protected override void OnSessionChanged() => scoringClock.Reset();

    protected override RaceSnapshot Decode(MemoryMappedViewAccessor view, DateTimeOffset now)
    {
        if (view.Capacity < Size) throw new InvalidDataException("LMU 共享内存布局过短");
        var page = new byte[Size]; // Retained by the immutable snapshot for on-demand field reads.
        var before = view.ReadInt64(40); // adjacent scoring and telemetry update counters
        view.ReadArray(0, page, 0, Size);
        if (before != BitConverter.ToInt64(page, 40) || before != view.ReadInt64(40))
            return Empty(now, SessionPhase.Connecting, "正在同步 LMU 遥测");
        return FromPage(page, now);
    }

    internal RaceSnapshot FromPage(byte[] data, DateTimeOffset now)
    {
        int I(int at) => BitConverter.ToInt32(data, at);
        double D(int at) => BitConverter.ToDouble(data, at);
        string Name(int at) => Encoding.UTF8.GetString(data, at, 64).Split('\0')[0].Trim();
        int count = I(Score + 104), active = data[Telemetry], index = data[Telemetry + 1];
        if (count is < 0 or > 104 || active > 104) throw new InvalidDataException("LMU 车辆表无效");
        if (data[Score + 108] == 9) return Empty(now, SessionPhase.Paused, "游戏已暂停");
        if (data[Score + 115] != 1 || data[Telemetry + 2] != 1 || active == 0 || index >= 104)
            return Empty(now, SessionPhase.Idle, "等待进入 LMU 驾驶座舱");
        int car = Telemetry + 4 + index * 1888;
        int id = I(car), player = -1;
        for (var n = 0; n < count; n++)
        {
            int row = Vehicles + n * 584;
            if (data[row + 196] == 1 && I(row) == id) { player = row; break; }
        }
        if (player < 0) return Empty(now, SessionPhase.Connecting, "正在匹配本车计时与物理数据");
        if (data[player + 197] == 3) return Empty(now, SessionPhase.Replay, "游戏正在重播");
        if (data[player + 197] != 0) return Empty(now, SessionPhase.Idle, "等待玩家接管赛车");

        var track = Name(Score); var model = Name(car + 32);
        var stamp = Advance($"{track}|{I(Score + 64)}|{I(24)}|{id}|{model}", D(car + 12), now);
        var score = scoringClock.Observe(D(Score + 68), now);
        if (stamp is not { } t) return Empty(now, SessionPhase.Connecting, "等待 LMU 物理帧更新");
        var fields = new Dictionary<string, Measurement>();
        void Physics(string key, double number, string unit, string source, double min, double max) =>
            Add(fields, key, number, unit, t, "LMU." + source, min, max);

        double x = D(car + 184), y = D(car + 192), z = D(car + 200);
        Physics("speed", Math.Sqrt(x*x + y*y + z*z) * 3.6, "km/h", "mLocalVel", 0, 600);
        Physics("fuel", D(car + 524), "L", "mFuel", 0, 500);
        Physics("gear", I(car + 352), "挡", "mGear", -1, 12);
        Physics("rpm", D(car + 356), "rpm", "mEngineRPM", 0, 30000);
        Physics("Physics.waterTemp", D(car + 364), "°C", "mEngineWaterTemp", -50, 220);
        for (var i = 0; i < 4; i++)
        {
            var w = Metrics.Wheels[i]; var at = car + 848 + 260 * i;
            Physics("temperature." + w, D(at + 204) - 273.15, "°C", $"mWheels[{i}].mTireCarcassTemperature", -50, 250);
            Physics("pressure." + w, D(at + 120) / 6.894757293168, "psi", $"mWheels[{i}].mPressure", 0.1, 100);
            Physics("Physics.brakeTemp." + w, D(at + 24), "°C", $"mWheels[{i}].mBrakeTemp", -50, 1600);
        }
        if (score is { } s)
        {
            void Scored(string key, double value, string unit, string source, double min, double max) =>
                Add(fields, key, value, unit, s, "LMU." + source, min, max);
            Scored("lapsBehindAhead", I(player + 240), "lap", "mLapsBehindNext", 0, 1000);
            Scored("position", data[player + 199], "position", "mPlace", 1, count);
            Scored("completedLaps", BitConverter.ToInt16(data,player + 100), "lap", "mTotalLaps", 0, 10000);
            Scored("lastLapTime", D(player + 168) * 1000, "ms", "mLastLapTime", 1, 3600000);
            Scored("inPitLane", data[player + 198], "boolean", "mInPits", 0, 1);
            Scored("airTemperature", D(Score + 228), "°C", "mAmbientTemp", -50, 80);
            Scored("roadTemperature", D(Score + 236), "°C", "mTrackTemp", -50, 100);
            if (D(Score + 76) > 0) Scored("sessionTimeLeft", D(Score + 76) - D(Score + 68), "s", "mEndET-mCurrentET", 0, 604800);
            if (I(player + 240) == 0 && data[player + 199] > 1)
                Scored("gapAhead", D(player + 232) * 1000, "ms", "mTimeBehindNext", 1, 3600000);
        }
        RaceTraffic? traffic = null;
        if (score is { } scored)
        {
            var grid = new List<RaceCar>();
            for (int n = 0; n < count; n++)
            {
                int row = Vehicles + n * 584;
                double vx = D(row + 288), vy = D(row + 296), vz = D(row + 304);
                double speed = Math.Sqrt(vx * vx + vy * vy + vz * vz) * 3.6;
                var name = Encoding.UTF8.GetString(data, row + 4, 32).Split('\0')[0].Trim();
                double distance = D(Score + 88);
                double? progress = distance > 0 && D(row + 104) >= 0 && D(row + 104) <= distance
                    ? BitConverter.ToInt16(data, row + 100) + D(row + 104) / distance : null;
                grid.Add(new(I(row), data[row + 199], name, scored,
                    double.IsFinite(speed) && speed is >= 0 and <= 600 ? speed : null, false, progress, data[row + 198] == 1));
            }
            var followers = Enumerable.Range(0, count).Select(n => Vehicles + n * 584)
                .Where(row => data[row + 199] == data[player + 199] + 1).Take(2).ToArray();
            if (followers.Length == 1 && I(followers[0] + 240) == 0)
                Add(fields, "gapBehind", D(followers[0] + 232) * 1000, "ms", scored, "LMU.follower.mTimeBehindNext", 0, 3600000);
            bool race = I(Score + 64) is >= 10 and <= 13;
            traffic = new(id, grid, scored, race && data[Score + 108] is 5 or 6, race && data[Score + 108] == 8);
            if (race && I(player + 252) == 0)
                Add(fields, "gapLeader", D(player + 244), "s", scored, "LMU.mTimeBehindLeader", 0, 3600);
            if (race) Add(fields, "lapsBehindLeader", I(player + 252), "lap", scored, "LMU.mLapsBehindLeader", 0, 1000);
        }
        var native = new LmuNativeFrame(data,car,player,count,t,score);
        // Sector flag codes vary by build. Use the documented full-course signals.
        var yellowPhase = (sbyte)data[Score + 109];
        var fullCourse = data[Score + 108] == 6 || yellowPhase is >= 2 and <= 5;
        var penalties = BitConverter.ToInt16(data, player + 194);
        var control = score is { } observed ? new RaceControlState(fullCourse, penalties >= 0 ? penalties : null, observed) { Blue = data[player + 504] == 6 } : null;
        return Live(now, model, track, I(44), fields) with
        { RaceControl = control, Traffic = traffic, Native = new(LmuSchema.Fields,native.Read,score.HasValue ? $"计时已连接 · {count} 辆参赛车" : "等待计时更新") };
    }
}
