using DeltaTR.Core;

namespace DeltaTR.Telemetry.ACC.Broadcast;

internal sealed class BroadcastState
{
    private HashSet<ushort> members = [];
    private readonly Dictionary<ushort, Entry> entries = [];
    private readonly Dictionary<ushort, (Car Car, DateTimeOffset Time)> cars = [];
    private (Session Value, DateTimeOffset Time)? session;
    private RaceEvent? lastEvent;
    private Track? track;
    internal bool NeedsEntries { get; private set; } = true;
    internal bool NeedsTrack => track is null;

    // True requests new metadata immediately at an event/session boundary.
    internal bool Apply(BroadcastPacket packet, int connection, DateTimeOffset now)
    {
        switch (packet)
        {
            case Entries list when list.Id == connection:
                members = list.Cars.ToHashSet();
                foreach (var id in entries.Keys.Except(members).ToArray()) entries.Remove(id);
                foreach (var id in cars.Keys.Except(members).ToArray()) cars.Remove(id);
                NeedsEntries = members.Any(id => !entries.ContainsKey(id));
                break;
            case Entry entry when members.Contains(entry.Id):
                entries[entry.Id] = entry;
                NeedsEntries = members.Any(id => !entries.ContainsKey(id));
                break;
            case Track t when t.Connection == connection:
                track = t;
                break;
            case Session s:
                var changed = session is { } old && (s.Index != old.Value.Index || s.Event != old.Value.Event ||
                    s.Type != old.Value.Type || s.Replay != old.Value.Replay || s.Elapsed + 3000 < old.Value.Elapsed);
                if (changed)
                {
                    cars.Clear(); entries.Clear(); members.Clear(); lastEvent = null; track = null; NeedsEntries = true;
                }
                session = (s, now);
                return changed;
            case Car car when members.Contains(car.Id):
                if (cars.TryGetValue(car.Id, out var previous) && now - previous.Time > Metrics.MaxAge)
                    entries.Remove(car.Id);
                cars[car.Id] = (car, now);
                if (!entries.TryGetValue(car.Id, out var identity) || identity.Drivers.Length != car.Drivers)
                { entries.Remove(car.Id); NeedsEntries = true; }
                break;
            case Car:
                NeedsEntries = true;
                break;
            case RaceEvent e:
                lastEvent = e;
                break;
        }
        return false;
    }

    internal BroadcastFrame Snapshot(RaceSnapshot shared, DateTimeOffset now)
    {
        if (session is not { } frame || now - frame.Time > Metrics.MaxAge)
            return BroadcastFrame.Empty("广播已注册，等待实时赛事数据");
        var s = frame.Value;
        if (s.Replay)
            return BroadcastFrame.Empty($"广播已连接 · 重播进度 {Seconds(s.ReplayTime)}，剩余 {Seconds(s.ReplayLeft)}");
        if (s.Type == 14 || s.Phase is 0 or > 6 || shared.Phase is SessionPhase.Paused or SessionPhase.Replay)
            return BroadcastFrame.Empty("广播已连接，等待驾驶会话");
        var fields = new Dictionary<string, Measurement>();
        void Set(string key, string? value, DateTimeOffset observed, string? imperial = null)
        {
            if (!string.IsNullOrWhiteSpace(value))
                fields["UDP." + key] = new(null, "", observed, DataQuality.Valid, "ACC.Broadcast." + key, "v4") { TextValue = value, ImperialText = imperial };
        }
        void Report(string key, string? value) => Set(key, value, frame.Time);
        Report("session", $"赛事 {s.Event}，场次 {s.Index}，{SessionName(s.Type)}，阶段代码 {s.Phase}。");
        Report("timing", $"已进行 {Seconds(s.Elapsed)}，赛段结束时间读数 {Seconds(s.End)}，赛道时钟 {Seconds(s.Clock)}。");
        Report("camera", $"焦点车辆 {s.Focus}，机位组 {s.CameraSet}，镜头 {s.Camera}，界面 {s.Hud}。");
        var conditions = $"云量 {s.Clouds * 10}%，雨量 {s.Rain * 10}%，赛道湿润度 {s.Wet * 10}%。";
        Set("weather", $"气温 {s.Air} °C，路温 {s.Road} °C，{conditions}", frame.Time,
            $"气温 {s.Air * 1.8 + 32:0.#} °F，路温 {s.Road * 1.8 + 32:0.#} °F，{conditions}");
        Report("best", DescribeLap(s.Best));
        if (track is { } t)
        {
            Report("track", $"{t.Name}，赛道编号 {t.Id}，长度 {t.Length} 米。");
            Report("cameras", string.Join("、", t.Cameras));
            Report("hud", string.Join("、", t.HudPages));
        }
        if (lastEvent is { } e)
            Report("event", $"{EventName(e.Kind)}，车辆编号 {e.CarId}，赛事时间 {Seconds(e.Time)}。{e.Message}");
        Report("entries", string.Join("；", members.Order().Select(id => entries.TryGetValue(id, out var entry)
            ? $"{entry.Number}号 {entry.Team}（编号{id}）" : $"车辆编号{id}")));
        var active = cars.Values.Where(c => now - c.Time <= Metrics.MaxAge && c.Car.Position > 0)
            .OrderBy(c => c.Car.Position).ToArray();
        if (active.Length > 0)
        {
            var oldest = active.Min(c => c.Time);
            Set("standings", string.Join("，", active.Select(c => $"第{c.Car.Position}名 {Number(c.Car.Id)}")), oldest);
            Set("sectors", SectorRanks(active.Select(c => c.Car).ToArray()), oldest);
        }
        var player = shared.Reading("Graphics.playerCarID");
        var sharedSession = shared.Reading("Graphics.sessionIndex");
        var sameSession = shared.Phase == SessionPhase.Live && sharedSession?.Value == s.Index &&
            sharedSession.IsUsable(now, Metrics.MaxAge) && track is not null &&
            string.Equals(shared.Track, track.Name, StringComparison.OrdinalIgnoreCase);
        int? selfPosition = null;
        if (sameSession && player?.IsUsable(now, Metrics.MaxAge) == true)
            selfPosition = active.FirstOrDefault(c => c.Car.Id == player.Value).Car?.Position;

        foreach (var group in active.GroupBy(c => c.Car.Position))
        {
            if (group.Count() != 1) continue;
            var (car, observed) = group.First();
            var details = Details(car);
            var imperialMotion = Motion(car, $"{car.Speed / 1.609344:0.#} mph");
            Set("rank." + car.Position, string.Join(" ", details.Values), observed,
                string.Join(" ", details.Select(p => p.Key == "motion" ? imperialMotion : p.Value)));
            void Target(string target)
            {
                foreach (var (key, value) in details) Set(target + "." + key, value, observed, key == "motion" ? imperialMotion : null);
            }
            if (car.Position == 1) Target("leader");
            if (selfPosition == car.Position) Target("self");
            if (selfPosition - 1 == car.Position) Target("ahead");
            if (selfPosition + 1 == car.Position) Target("behind");
        }
        RaceTraffic? traffic = null;
        if (sameSession && player?.IsUsable(now, Metrics.MaxAge) == true && player.Value is double playerId)
        {
            var grid = active.Select(c => new RaceCar(c.Car.Id, c.Car.Position, Number(c.Car.Id), c.Time,
                c.Car.Speed, false, c.Car.Spline is >= 0 and <= 1 ? c.Car.Laps + c.Car.Spline : null,
                c.Car.Location is 2 or 3)).ToArray();
            // ACC broadcasting Race=10; Session=5, SessionOver=6, PostSession=7.
            traffic = new((int)playerId, grid, frame.Time, s.Type == 10 && s.Phase == 5, s.Type == 10 && s.Phase >= 6);
        }
        return new($"广播已连接 · {active.Length} 辆车有实时排名", true, fields) { Traffic = traffic };
    }

    private Dictionary<string, string> Details(Car c)
    {
        var d = new Dictionary<string, string>();
        if (entries.TryGetValue(c.Id, out var entry))
        {
            var roster = entry.Drivers;
            if (c.Driver < roster.Length) d["driver"] = Name(roster[c.Driver]);
            d["entry"] = $"{entry.Number}号，{entry.Team}，车辆编号 {c.Id}，车型代码 {entry.Model}，组别 {entry.Cup}，国籍代码 {entry.Nation}，报名车手索引 {entry.Driver}。";
            d["roster"] = string.Join("；", roster.Select((p, i) => $"车手{i} {Name(p)}，简称 {p.Short}，等级 {p.Category}，国籍代码 {p.Nation}"));
        }
        d["position"] = $"{Number(c.Id)}，总排名 {c.Position}，组别排名 {c.Cup}，赛道顺序 {c.TrackPosition}，已跑 {c.Laps} 圈。";
        d["motion"] = Motion(c, $"{c.Speed} km/h");
        foreach (var (key, lap) in new[] { ("best", c.Best), ("last", c.Last), ("current", c.Current) })
            if (DescribeLap(lap) is { } text) d[key] = (key == "best" ? "最佳圈，" : key == "last" ? "上一圈，" : "当前圈，") + text;
        return d;
    }

    private static string Motion(Car c, string speed) =>
        $"实时车手索引 {c.Driver}，共 {c.Drivers} 位，车速 {speed}，挡位 {c.Gear}，{Location(c.Location)}，圈进度 {c.Spline:P1}，平面坐标 {c.X:0.##}、{c.Y:0.##}，航向 {c.Yaw:0.###} rad，圈速差 {Seconds(c.Delta)}。";

    private string Number(ushort id) => entries.TryGetValue(id, out var e) ? $"{e.Number}号车" : $"车辆编号{id}";
    private static string Name(Driver d) => string.IsNullOrWhiteSpace(d.First + d.Last) ? d.Short : (d.First + " " + d.Last).Trim();
    private static string Seconds(double ms) => ms == int.MaxValue ? "暂无计时" : $"{ms / 1000:0.###} 秒";
    private static bool HasTime(int ms) => ms is > 0 and < int.MaxValue;
    private static string? DescribeLap(Lap lap)
    {
        if (!HasTime(lap.Time) && !lap.Splits.Any(HasTime) && !lap.Out && !lap.In && !lap.Invalid) return null;
        var splits = string.Join("、", lap.Splits.Select(t => HasTime(t) ? Seconds(t) : "暂无"));
        return $"车辆编号 {lap.Car}，车手索引 {lap.Driver}，圈时 {(HasTime(lap.Time) ? Seconds(lap.Time) : "未完成")}，分段 {splits}。" +
            (lap.Invalid ? "圈速无效。" : "") + (lap.PersonalBestEligible ? "可计入最佳圈。" : "未计入最佳圈。") +
            (lap.Out ? "出站圈。" : "") + (lap.In ? "进站圈。" : "");
    }

    private string? SectorRanks(Car[] active)
    {
        var laps = active.Where(c => !c.Best.Invalid && c.Best.PersonalBestEligible).ToArray();
        var reports = new List<string>();
        for (var i = 0; i < laps.Select(c => c.Best.Splits.Length).DefaultIfEmpty().Max(); i++)
        {
            var ranked = laps.Where(c => c.Best.Splits.Length > i && HasTime(c.Best.Splits[i])).OrderBy(c => c.Best.Splits[i]);
            var order = string.Join("、", ranked.Select(c => $"{Number(c.Id)} {Seconds(c.Best.Splits[i])}"));
            if (order.Length > 0) reports.Add($"第{i + 1}段 {order}");
        }
        return reports.Count == 0 ? null : "按有有效数据车辆的各自最佳圈分段排序：" + string.Join("；", reports);
    }
    private static string Location(byte code) => code switch { 1 => "在赛道", 2 => "在维修区", 3 => "正在进站", 4 => "正在出站", _ => $"位置代码 {code}" };
    private static string SessionName(byte code) => code switch { 0 => "练习", 4 => "排位", 9 => "超级排位", 10 => "比赛", 11 => "热圈", 12 => "长距离计时", 13 => "热圈超级排位", _ => $"类型 {code}" };
    private static string EventName(byte code) => code switch { 1 => "绿旗", 2 => "阶段结束", 3 => "判罚通知", 4 => "事故", 5 => "完成一圈", 6 => "全场最快圈", 7 => "个人最快圈", _ => $"赛事事件 {code}" };
}
