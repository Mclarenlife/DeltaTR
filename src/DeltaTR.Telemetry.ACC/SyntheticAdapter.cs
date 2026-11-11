using System.Diagnostics;
using DeltaTR.Core;
using DeltaTR.Telemetry.Abstractions;

namespace DeltaTR.Telemetry.ACC;

/// <summary>Deterministic synthetic source for UI/replay testing. Never passed off as live ACC data.</summary>
public sealed class SyntheticAdapter(RacingGame game = RacingGame.ACC) : IGameAdapter
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly string session = "synthetic-" + Guid.NewGuid().ToString("N");
    private int packet;
    public RaceSnapshot Read() => Create(packet++, clock.ElapsedMilliseconds, DateTimeOffset.UtcNow, session, game);

    public static RaceSnapshot Create(int step, long elapsed, DateTimeOffset now, string session = "synthetic-fixture", RacingGame game = RacingGame.ACC)
    {
        var fields = new Dictionary<string, Measurement>();
        void Add(string id, double value, string unit) => fields[id] = new(value, unit, now, DataQuality.Valid, "Synthetic." + id, "DEMO");
        Add("speed", 160 + 60 * Math.Sin(step / 20d), "km/h");
        Add("fuel", Math.Max(0, 45 - elapsed / 45000d), "L");
        Add("airTemperature", 24, "°C"); Add("roadTemperature", 32, "°C");
        long second = elapsed / 1000 % 90;
        bool yellow = second is >= 15 and < 23;
        int rank = second is >= 30 and < 70 ? 4 : second >= 85 ? 6 : 5;
        int penalty = second is >= 45 and < 60 ? 1 : 0;
        Add("position", rank, "position"); Add("completedLaps", Math.Floor(elapsed / 107200d), "lap");
        Add("lastLapTime", 107200, "ms"); Add("gapAhead", 1840, "ms");
        Add("gapBehind", 2300, "ms"); Add("sessionTimeLeft", Math.Max(0, 1200000 - elapsed), "ms");
        Add("Physics.waterTemp", 85 + 2 * Math.Sin(step / 30d), "°C");
        Add("Graphics.TC", 3, "档"); Add("Graphics.ABS", 2, "档");
        Add("Graphics.EngineMap", 1, "档"); Add("Graphics.mfdFuelToAdd", 30, "L");
        Add("Graphics.iBestTime", 105240, "ms");
        Add("rainIntensity", 0, "category"); Add("missingMandatoryPits", 1, "count");
        for (var i = 0; i < 4; i++)
        {
            Add($"Physics.brakeTemp.{Metrics.Wheels[i]}", 480 + i * 9 + 50 * Math.Sin(step / 25d), "°C");
            Add($"pressure.{Metrics.Wheels[i]}", 26.5 + i * 0.1 + 0.08 * Math.Sin(step / 15d), "psi");
            Add($"temperature.{Metrics.Wheels[i]}", 81 + i * 2 + 2 * Math.Sin(step / 30d), "°C");
        }
        if (game == RacingGame.ACC)
        {
            Add("Graphics.flag", yellow ? 2 : 0, "代码");
            Add("Graphics.penalty", penalty, "代码");
        }
        if (game != RacingGame.ACC)
        {
            foreach (var key in new[] { "Graphics.TC","Graphics.ABS","Graphics.EngineMap","Graphics.mfdFuelToAdd","Graphics.iBestTime",
                "rainIntensity","missingMandatoryPits","gapBehind" }) fields.Remove(key);
            Add("gear", 4, "挡"); Add("rpm", 6800, "rpm"); Add("inPitLane", 0, "boolean");
        }
        if (game == RacingGame.IRacing)
        {
            fields.Remove("gapAhead");
            foreach (var w in Metrics.Wheels)
            {
                fields.Remove("pressure." + w); fields.Remove("temperature." + w); fields.Remove("Physics.brakeTemp." + w);
                Add("coldPressure." + w, 25.5, "psi"); Add("pitTemp." + w, 75, "°C"); Add("pitTread." + w, 89, "%");
            }
        }
        var car = game == RacingGame.LMU ? "Hypercar · 示例" : game == RacingGame.IRacing ? "GT3 · 示例" : "BMW M4 GT3 · 示例";
        var track = game == RacingGame.LMU ? "Le Mans · 示例" : game == RacingGame.IRacing ? "Daytona · 示例" : "Monza · 示例";
        double ownSpeed = fields["speed"].Value!.Value;
        var grid = new RaceCar[]
        {
            new(10, 1, "示例领跑车手", now, ownSpeed + 5),
            new(14, rank == 4 ? 5 : 4, "示例前车车手", now, ownSpeed + 12),
            new(15, rank, "示例本车车手", now, ownSpeed),
            new(16, rank == 6 ? 5 : 6, "示例后车车手", now, ownSpeed + 8)
        };
        Add("gapLeader", 12.4, "s");
        NativeTelemetry? native = null;
        if (game != RacingGame.ACC)
        {
            var reports = SimulatorRace.Reports(grid.Select(c => new SimulatorRace.Car(c.Id, c.Position, c.Name)),
                15, yellow ? "全场黄旗" : "绿旗");
            Measurement? Read(string key)
            {
                if (reports.TryGetValue(key,out var report)) return SimulatorRace.Text(report,now,"Synthetic."+key);
                return key switch
                {
                    "LMU.Physics.mVirtualEnergy" => new(68.5,"%",now,DataQuality.Valid,"Synthetic.virtualEnergy","DEMO"),
                    "LMU.Physics.mEngineOilTemp" or "IR.Var.OilTemp" => new(94.2,"°C",now,DataQuality.Valid,"Synthetic.oil","DEMO"),
                    "IR.Var.CarIdxPosition" => SimulatorRace.Text($"[14] {rank - 1}；[15] {rank}；[16] {rank + 1}",now,"Synthetic.CarIdxPosition"),
                    _ => null
                };
            }
            native = new(game==RacingGame.LMU?LmuSchema.Fields:IRacingChannels.ReferenceFields,Read,"演示计时数据");
        }
        return new RaceSnapshot(1, session, now, elapsed, SessionPhase.Live, DataOrigin.Synthetic, "DEMO", "DEMO", car, track, step, step,
            $"{game.Display()} 演示模式 · 全部数据人工生成", fields) { Game = game, Native = native, Traffic = new(15, grid, now, elapsed >= 4000, false), RaceControl = new(yellow, penalty, now) };
    }
    public void Dispose() { }
}
