using System.IO.MemoryMappedFiles;
using System.Text;
using DeltaTR.Core;
using YamlDotNet.RepresentationModel;

namespace DeltaTR.Telemetry.Simulators;

public sealed class IRacingAdapter() : MappedGameAdapter(RacingGame.IRacing, @"Local\IRSDKMemMapFileName")
{
    private Dictionary<string, SdkVariable> variables = new(StringComparer.Ordinal);
    private Dictionary<string,string> sessionFields = [];
    private Dictionary<int,string> driverNames = [];
    private int driverIndex = -1;
    private readonly Dictionary<int, bool> raceSessions = [];
    private double? trackLength;
    private IReadOnlyList<NativeField> catalog = [];
    private bool rebuildCatalog;
    private (int Count, int Offset, int Length) schema;
    private int? metadataVersion;
    private string track = "", car = "", weekend = "";

    internal RaceSnapshot ReadMap(MemoryMappedViewAccessor view, DateTimeOffset now) => Decode(view, now);
    protected override RaceSnapshot Decode(MemoryMappedViewAccessor v, DateTimeOffset now)
    {
        if (v.Capacity < 112 || v.ReadInt32(0) is not (1 or 2)) throw new InvalidDataException("不支持的 iRacing SDK 版本");
        if ((v.ReadInt32(4) & 1) == 0)
        {
            OnMappingClosed();
            return Empty(now, SessionPhase.Idle, "iRacing 等待模拟器连接");
        }
        int count = v.ReadInt32(24), offset = v.ReadInt32(28), buffers = v.ReadInt32(32), length = v.ReadInt32(36);
        if (count is < 1 or > 16384 || buffers is < 1 or > 4 || length is < 1 or > 1048576 ||
            offset < 0 || offset + 144L * count > v.Capacity) throw new InvalidDataException("iRacing 变量表无效");
        if (variables.Count == 0 || schema != (count, offset, length) || metadataVersion != v.ReadInt32(12))
        {
            variables = new(StringComparer.Ordinal); rebuildCatalog = true;
            var header = new byte[144];
            for (var i = 0; i < count; i++)
            {
                v.ReadArray(offset + 144L*i, header, 0, 144);
                int type = BitConverter.ToInt32(header), at = BitConverter.ToInt32(header, 4);
                int n = BitConverter.ToInt32(header, 8);
                int width = SdkVariable.Width(type);
                if (n < 1 || width == 0 || at < 0 || at + (long)n*width > length) continue;
                var name = Text(header,16,32);
                if (name.Length > 0) variables[name] = new(type, at, n, Text(header,112,32), Text(header,48,64), header[12] != 0);
            }
            schema = (count, offset, length);
        }
        LoadWeekend(v);
        if (rebuildCatalog)
        {
            var next = variables.Select(p => IRacingChannels.Variable(p.Key, p.Value.Type, p.Value.Count,
                    p.Value.CountAsTime, p.Value.Unit, p.Value.Description))
                .Concat(variables.Where(p => p.Key.StartsWith("CarIdx", StringComparison.Ordinal) && p.Value.Type != 0)
                    .Select(p => IRacingChannels.PlayerField(p.Key)))
                .Concat(sessionFields.Keys.Select(k=>IRacingChannels.SessionField(k[11..])))
                .Concat(SimulatorRace.Fields).ToArray();
            if (!catalog.SequenceEqual(next)) catalog=next;
            rebuildCatalog=false;
        }
        var row = new byte[length];
        int latest = 48;
        for (var i = 1; i < buffers; i++)
        {
            int candidate = 48 + 16*i;
            if (unchecked(v.ReadInt32(candidate) - v.ReadInt32(latest)) > 0) latest = candidate;
        }
        int tick = v.ReadInt32(latest), start = v.ReadInt32(latest + 4);
        if (start < 0 || start + (long)length > v.Capacity) throw new InvalidDataException("iRacing 帧越界");
        v.ReadArray(start, row, 0, length);
        if (tick != v.ReadInt32(latest) || (v.ReadInt32(4) & 1) == 0 || v.ReadInt32(12) != metadataVersion)
            return Empty(now, SessionPhase.Connecting, "正在同步 iRacing 帧");
        double? Get(string name, string? unit = null)
        {
            if (!variables.TryGetValue(name, out var d) || d.Count != 1 || (unit != null && d.Unit != unit)) return null;
            return d.Type switch { 1 => row[d.Offset], 2 => BitConverter.ToInt32(row, d.Offset),
                4 => BitConverter.ToSingle(row, d.Offset), 5 => BitConverter.ToDouble(row, d.Offset), _ => null };
        }
        RaceSnapshot Waiting(SessionPhase phase,string message) => Empty(now,phase,message) with
        { Native = new(catalog,_=>null,message) };
        if (Get("IsReplayPlaying") == 1) return Waiting(SessionPhase.Replay, "iRacing 正在重播");
        if (Get("IsOnTrack") != 1) return Waiting(SessionPhase.Idle, "请进入 iRacing 驾驶座舱");
        var sessionTime = Get("SessionTime", "s");
        if (sessionTime is not double time || !double.IsFinite(time)) return Waiting(SessionPhase.Connecting, "等待 iRacing 会话时钟");
        var stamp = Advance($"{weekend}|{track}|{car}|{Get("SessionNum")}", time, now);
        if (stamp is not { } t) return Waiting(SessionPhase.Connecting, "等待 iRacing 驾驶帧更新");
        var fields = new Dictionary<string, Measurement>();
        void Read(string key, string name, string? nativeUnit, string unit, double min, double max, double scale = 1) =>
            Add(fields, key, Get(name, nativeUnit) * scale, unit, t, "iRacing." + name, min, max);
        Read("speed", "Speed", "m/s", "km/h", 0, 600, 3.6);
        Read("fuel", "FuelLevel", "l", "L", 0, 500);
        Read("rpm", "RPM", "revs/min", "rpm", 0, 30000);
        Read("gear", "Gear", null, "挡", -1, 15);
        Read("position", "PlayerCarPosition", null, "position", 1, 200);
        Read("completedLaps", "LapCompleted", null, "lap", 0, 10000);
        Read("lastLapTime", "LapLastLapTime", "s", "ms", 1, 3600000, 1000);
        Read("airTemperature", "AirTemp", "C", "°C", -50, 80);
        Read("roadTemperature", variables.ContainsKey("TrackTempCrew") ? "TrackTempCrew" : "TrackTemp", "C", "°C", -50, 100);
        Read("sessionTimeLeft", "SessionTimeRemain", "s", "s", 0, 604800);
        Read("inPitLane", "OnPitRoad", null, "boolean", 0, 1);
        Read("Physics.waterTemp", "WaterTemp", "C", "°C", -50, 220);
        string[] corners = ["LF","RF","LR","RR"];
        for (var i = 0; i < 4; i++)
        {
            var w = Metrics.Wheels[i];
            Read("coldPressure." + w, corners[i] + "coldPressure", "kPa", "psi", 0.1, 100, 1/6.894757293168);
            Read("pitTemp." + w, corners[i] + "tempCM", "C", "°C", -50, 250);
            Read("pitTread." + w, corners[i] + "wearM", "%", "%", 0, 100, 100);
        }
        var player = Get("PlayerCarIdx") ?? driverIndex;
        uint? Flags(string name, int index) => variables.TryGetValue(name, out var channel) &&
            channel.Type == 3 && index >= 0 && index < channel.Count ? (uint)channel.Number(row, index) : null;
        var sessionFlags = Flags("SessionFlags", 0);
        var carFlags = Flags("CarIdxSessionFlags", (int)player);
        var flags = sessionFlags ?? carFlags;
        uint? personal = sessionFlags.HasValue || carFlags.HasValue ? sessionFlags.GetValueOrDefault() | carFlags.GetValueOrDefault() : null;
        const uint yellowFlags = 0x8 | 0x100 | 0x4000 | 0x8000;
        int? penalty = personal is { } bits ? (bits & 0x20000) != 0 ? 2 : (bits & 0x10000) != 0 ? 1 : 0 : null;
        var control = new RaceControlState(flags is uint raceFlags ? (raceFlags & yellowFlags) != 0 : null, penalty, t)
            { Blue = flags is uint shownFlags ? (shownFlags & 0x20) != 0 : null };
        double? CarValue(string key, int index) => variables.TryGetValue(key, out var value) &&
            index >= 0 && index < value.Count && value.Type is 1 or 2 or 4 or 5 &&
            double.IsFinite(value.Number(row, index)) ? value.Number(row, index) : null;
        var grid = new List<RaceCar>();
        if (variables.TryGetValue("CarIdxPosition", out var positions))
            for (int index = 0; index < Math.Min(positions.Count, 256); index++)
            {
                var rank = CarValue("CarIdxPosition", index);
                if (rank is not (>= 1 and <= 255)) continue;
                var lap = CarValue("CarIdxLapCompleted", index) ?? CarValue("CarIdxLap", index) - 1;
                var fraction = CarValue("CarIdxLapDistPct", index);
                double? progress = lap >= 0 && fraction is >= 0 and <= 1 ? lap + fraction : null;
                grid.Add(new(index, (int)rank, driverNames.GetValueOrDefault(index) ?? "", t,
                    index == player ? Get("Speed", "m/s") * 3.6 : null, false, progress, CarValue("CarIdxOnPitRoad", index) == 1));
            }
        bool? race = raceSessions.TryGetValue((int)(Get("SessionNum") ?? -1), out var isRace) ? isRace : null;
        var state = Get("SessionState");
        var traffic = new RaceTraffic((int)player, grid, t,
            race.HasValue && state.HasValue ? race.Value && state == 4 : null,
            race.HasValue && state.HasValue ? race.Value && state is 5 or 6 : null, trackLength);
        if (traffic.Player is { Position: > 0 } own)
            Add(fields, "position", own.Position, "position", t, "iRacing.CarIdxPosition", 1, 255);
        if (race == true && traffic.Player is { } self)
        {
            var gap = CarValue("CarIdxF2Time", self.Id);
            Add(fields, "gapLeader", gap, "s", t, "iRacing.CarIdxF2Time", 0, 3600);
            if (traffic.AtPosition(self.Position + 1) is { } behind)
                Add(fields, "gapBehind", (CarValue("CarIdxF2Time", behind.Id) - gap) * 1000, "ms", t, "iRacing.CarIdxF2Time", 0, 3600000);
            if (traffic.AtPosition(self.Position - 1) is { } ahead)
                Add(fields, "gapAhead", (gap - CarValue("CarIdxF2Time", ahead.Id)) * 1000, "ms", t, "iRacing.CarIdxF2Time", 0, 3600000);
        }
        var detail = new IRacingNativeFrame(row,variables,sessionFields,driverNames,(int)player,t);
        return Live(now, car, track, tick, fields) with { RaceControl = control, Traffic = traffic, Native = new(catalog,detail.Read,
            variables.ContainsKey("CarIdxPosition") ? "SDK 计时已连接" : "等待 SDK 提供车辆排名数组") };
    }

    private static string Text(byte[] bytes, int at, int length) =>
        Encoding.ASCII.GetString(bytes, at, length).Split('\0')[0];

    private void LoadWeekend(MemoryMappedViewAccessor v)
    {
        var version = v.ReadInt32(12);
        if (version == metadataVersion) return;
        var len = v.ReadInt32(16); var offset = v.ReadInt32(20);
        if (offset < 0 || len < 0 || len > 4194304 || offset + (long)len > v.Capacity) throw new InvalidDataException("iRacing 会话描述越界");
        var bytes = new byte[len]; v.ReadArray(offset, bytes, 0, len);
        if (v.ReadInt32(12) != version) return;
        raceSessions.Clear(); trackLength = null;
        metadataVersion = version; track = car = weekend = ""; sessionFields=[]; driverNames=[]; driverIndex=-1; rebuildCatalog=true;
        if (len == 0) return;
        try
        {
            var yaml = new YamlStream();
            yaml.Load(new StringReader(IRacingSessionText.Decode(bytes)));
            var root = (YamlMappingNode)yaml.Documents[0].RootNode;
            static YamlNode? Node(YamlNode? node, string key) =>
                node is YamlMappingNode m && m.Children.TryGetValue(key, out var value) ? value : null;
            static string Scalar(YamlNode? node) => (node as YamlScalarNode)?.Value ?? "";
            sessionFields = FlattenSession(root);
            var info = Node(root, "WeekendInfo");
            track = Scalar(Node(info, "TrackDisplayName")); weekend = Scalar(Node(info, "SubSessionID"));
            var lengthText = Scalar(Node(info, "TrackLength"));
            if (lengthText.EndsWith(" km", StringComparison.Ordinal) &&
                double.TryParse(lengthText[..^3], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var km) && km is > 0 and < 100)
                trackLength = km * 1000;
            if (Node(Node(root, "SessionInfo"), "Sessions") is YamlSequenceNode sessions)
                foreach (var item in sessions.Children)
                    if (int.TryParse(Scalar(Node(item, "SessionNum")), out var sessionNumber))
                        raceSessions[sessionNumber] = Scalar(Node(item, "SessionType")).Equals("Race", StringComparison.OrdinalIgnoreCase);
            var drivers = Node(root, "DriverInfo");
            var player = Scalar(Node(drivers, "DriverCarIdx"));
            if (int.TryParse(player,out var id)) driverIndex=id;
            if (Node(drivers,"Drivers") is YamlSequenceNode list)
                foreach (var driver in list.Children)
                {
                    if (!int.TryParse(Scalar(Node(driver,"CarIdx")),out var idx) || idx<0) continue;
                    driverNames[idx] = Scalar(Node(driver,"UserName"));
                    if (idx==driverIndex) car=Scalar(Node(driver,"CarScreenName"));
                }
        }
        catch (Exception e) when (e is YamlDotNet.Core.YamlException or InvalidCastException or ArgumentOutOfRangeException)
        { /* A partial metadata document must not prevent reading SDK telemetry. */ }
    }

    private static Dictionary<string,string> FlattenSession(YamlNode root)
    {
        // Group repeated entries by SDK path, retaining their sequence indices.
        var leaves = new Dictionary<string,List<string>>();
        var visiting = new HashSet<YamlNode>(ReferenceEqualityComparer.Instance);
        void Walk(YamlNode node,string path,string index,int depth)
        {
            if (depth>48 || !visiting.Add(node)) return;
            try
            {
                if (node is YamlMappingNode map)
                {
                    foreach (var (k,v) in map.Children)
                    {
                        if (k is not YamlScalarNode { Value: { } name } || name.Length==0) continue;
                        if (!IRacingReference.IncludeSessionField(name)) continue;
                        Walk(v,path+"."+name,index,depth+1);
                    }
                }
                else if (node is YamlSequenceNode seq)
                    for (int i=0;i<seq.Children.Count;i++) Walk(seq.Children[i],path,index+$"[{i}]",depth+1);
                else if (node is YamlScalarNode { Value: { Length: >0 } text })
                {
                    if (!leaves.TryGetValue(path,out var values)) leaves[path]=values=[];
                    values.Add(index.Length==0?text:index+" "+text);
                }
            }
            finally { visiting.Remove(node); }
        }
        Walk(root,"IR.Session","",0);
        return leaves.ToDictionary(p=>p.Key,p=>string.Join("；",p.Value));
    }

    protected override void OnMappingClosed()
    { raceSessions.Clear(); trackLength = null; variables=new(StringComparer.Ordinal); metadataVersion=null; sessionFields=[]; driverNames=[]; catalog=[]; }
}
