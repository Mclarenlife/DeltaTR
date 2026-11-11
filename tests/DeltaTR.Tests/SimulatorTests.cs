using System.IO.MemoryMappedFiles;
using System.Text;
using DeltaTR.Core;
using DeltaTR.Engineer;
using DeltaTR.Telemetry.Simulators;

internal static class SimulatorTests
{
    internal static void Lmu()
    {
        using var adapter = new LmuAdapter();
        var data = new byte[324820];
        void I(int offset, int value) => BitConverter.TryWriteBytes(data.AsSpan(offset), value);
        void D(int offset, double value) => BitConverter.TryWriteBytes(data.AsSpan(offset), value);
        var now = DateTimeOffset.UtcNow;
        // Two cars. Player is the second telemetry entry but FIRST scoring entry.
        const int score = 1632, first = 2192, telemetry = 128464, car = 130356;
        data[telemetry] = 2; data[telemetry+1] = 1; data[telemetry+2] = 1;
        I(score+64, 10); D(score+88, 5000); I(score+104, 2); data[score+115] = 1; data[score+108] = 5; I(first, 23); data[first+196] = 1; data[first+199] = 2;
        I(car,23); D(car+184,30); D(car+524,42); D(car+848+120,180); D(car+848+204,350);
        for (var i=1;i<4;i++) D(car+848+i*260+120,180+i);
        D(car+12,1); D(score+68,1); adapter.FromPage(data, now);
        D(car+12,2); D(score+68,2);
        var frame = adapter.FromPage(data, now.AddMilliseconds(100));
        Require(frame.Game == RacingGame.LMU && frame.Phase == SessionPhase.Live);
        Require(frame.Fields["speed"].Value == 108 && frame.Fields["fuel"].Value == 42 && frame.Fields["position"].Value == 2);
        Require(Math.Abs(frame.Fields["temperature.FL"].Value!.Value-76.85)<.001 && frame.Fields["pressure.FL"].Value is >26 and <26.2);
        I(first+584,24); data[first+584+199]=1; Encoding.UTF8.GetBytes("Rival").CopyTo(data,first+584+4);
        D(first+584+288, 40); D(first+584+104, 1600); D(first+104, 1400);
        D(first+244, 2.5);
        BitConverter.TryWriteBytes(data.AsSpan(car+776),.625f);
        frame=adapter.FromPage(data,frame.CapturedAt);
        Require(frame.Traffic is { Racing: true } && frame.Traffic.AtPosition(1)?.SpeedKph == 144);
        Require(frame.Fields["gapLeader"].Value == 2.5 && frame.Traffic!.Player?.Progress == .28);
        var q = new TelemetryQueries(new Dictionary<string,string[]>(), "", RacingGame.LMU);
        Require(q.Answer("虚拟能量还剩多少",frame,frame.CapturedAt).Text.Contains("62.5 %"));
        Require(q.Answer("前车是谁",frame,frame.CapturedAt).Text.Contains("Rival"));
        Require(q.Answer("比赛排名表",frame,frame.CapturedAt).Text.Contains("第2名"));
        Require(frame.Reading("LMU.Grid.Timing.mID")!.TextValue!.Contains("车辆24"));
        Require(q.Answer("现在是什么旗",frame,frame.CapturedAt).Text.Contains("绿旗"));
        Require(!q.Answer("前车是谁",frame,frame.CapturedAt.AddSeconds(3)).HasVerifiedData);
        Require(q.Answer("胎压",frame,frame.CapturedAt).HasVerifiedData);
        data[score+108] = 6;
        BitConverter.TryWriteBytes(data.AsSpan(first+194), (short)2);
        D(score+68,3);
        var control = adapter.FromPage(data,now.AddMilliseconds(200)).RaceControl;
        Require(control is { Yellow: true, Penalty: 2 });
        D(car+12,5);
        var frozenScoring = adapter.FromPage(data,now.AddSeconds(3));
        Require(frozenScoring.Fields.ContainsKey("fuel") && !frozenScoring.Fields.ContainsKey("position"));
        Require(!q.Answer("前车是谁", frozenScoring, frozenScoring.CapturedAt).HasVerifiedData);
        data[score+108]=9; Require(adapter.FromPage(data,now).Phase == SessionPhase.Paused);
        data[score+108]=5; data[first+197]=3; Require(adapter.FromPage(data,now).Phase == SessionPhase.Replay);
        data[first+197]=0; I(first, 99); Require(!adapter.FromPage(data,now).Fields.Any());
        I(first,23);
        var name = @"Local\DeltaTR_LMU_" + Guid.NewGuid().ToString("N");
        using var source = MemoryMappedFile.CreateNew(name,data.Length);
        using var writer = source.CreateViewAccessor();
        writer.WriteArray(0,data,0,data.Length);
        using var liveReader = new LmuAdapter(name);
        Require(liveReader.Read().Phase == SessionPhase.Connecting);
        writer.Write(car+12,6d); writer.Write(score+68,6d);
        Require(liveReader.Read().Fields["speed"].Value == 108);
        using var missing = new LmuAdapter(name + "_missing");
        Require(missing.Read().Phase == SessionPhase.Disconnected);
    }

    internal static void IRacing()
    {
        using var file = MemoryMappedFile.CreateNew(null, 16384);
        using var v = file.CreateViewAccessor();
        using var a = new IRacingAdapter();
        var now = DateTimeOffset.UtcNow;
        v.Write(0,2); v.Write(4,1); v.Write(12,1); v.Write(24,19); v.Write(28,160); v.Write(32,2); v.Write(36,160);
        v.Write(48,200); v.Write(52,4096); v.Write(64,201); v.Write(68,8192);
        void Bytes(int at,string s) { var b=Encoding.ASCII.GetBytes(s); v.WriteArray(at,b,0,b.Length); }
        void Column(int index,string name,int offset,int type,string unit)
        {
            var at=160+index*144; v.Write(at,type); v.Write(at+4,offset); v.Write(at+8,1); Bytes(at+16,name); Bytes(at+112,unit);
        }
        Column(0,"IsOnTrack",0,1,""); Column(1,"SessionTime",8,5,"s"); Column(2,"Speed",16,4,"m/s");
        Column(3,"FuelLevel",20,4,"l"); Column(4,"LFcoldPressure",24,4,"kPa"); Column(5,"IsReplayPlaying",2,1,"");
        Column(6,"LFwearM",28,4,"%"); Column(7,"LFtempCM",32,4,"C"); Column(8,"RPM",36,4,"wrong unit");
        Column(9,"SpecialHybrid",40,5,"kW"); Column(10,"CarIdxPosition",48,2,"");
        v.Write(160+10*144+8,3);
        Column(11,"SessionFlags",64,3,"irsdk_Flags"); Column(12,"Message",68,0,""); v.Write(160+12*144+8,12);
        Column(13,"ExtraSamples",88,4,"m/s"); v.Write(160+13*144+8,2);
        v.Write(8192+40,123.25d); v.Write(8192+48,1); v.Write(8192+52,2); v.Write(8192+64,0x28); Bytes(8192+68,"TEST");
        v.Write(8192+88,4.5f); v.Write(8192+92,-2.5f);
        Column(14,"SessionState",96,2,"irsdk_SessionState"); Column(15,"SessionNum",100,2,"");
        Column(16,"CarIdxF2Time",104,4,"s"); v.Write(160+16*144+8,3);
        Column(17,"CarIdxLapDistPct",116,4,"%"); v.Write(160+17*144+8,3);
        Column(18,"CarIdxLapCompleted",128,2,""); v.Write(160+18*144+8,3);
        v.Write(8192+96,3); v.Write(8192+108,4.5f);
        v.Write(8192+116,.4f); v.Write(8192+120,.3f);
        v.Write(8192+128,5); v.Write(8192+132,5);
        const string yaml="WeekendInfo:\n TrackDisplayName: Road America\n TrackLength: 5.00 km\n SubSessionID: 123\nSessionInfo:\n Sessions:\n - SessionNum: 0\n   SessionType: Race\nDriverInfo:\n DriverCarIdx: 1\n Drivers:\n - CarIdx: 0\n   CarScreenName: Other\n   UserName: Rival\n - CarIdx: 1\n   CarScreenName: Test GT3\n";
        v.Write(16,yaml.Length); v.Write(20,12000); Bytes(12000,yaml);
        v.Write(8192,(byte)1); v.Write(8200,10d); v.Write(8208,30f); v.Write(8212,25f); v.Write(8216,180f);
        v.Write(8220,.75f); v.Write(8224,82f);
        Require(a.ReadMap(v,now).Phase == SessionPhase.Connecting);
        v.Write(64,202); v.Write(8200,11d);
        var frame=a.ReadMap(v,now.AddMilliseconds(100));
        Require(frame.Phase == SessionPhase.Live && frame.Game == RacingGame.IRacing && frame.CarModel=="Test GT3" && frame.Track=="Road America");
        Require(frame.Fields["speed"].Value==108 && frame.Fields["pitTread.FL"].Value==75 && frame.Fields["pitTemp.FL"].Value==82);
        Require(frame.Traffic is { Racing: false, TrackLength: 5000 } && frame.Fields["gapLeader"].Value == 4.5);
        Require(!frame.Fields.ContainsKey("temperature.FL") && !frame.Fields.ContainsKey("rpm"));
        var q = new TelemetryQueries(new Dictionary<string,string[]>(), "", RacingGame.IRacing);
        Require(q.Answer("左前冷胎压多少",frame,frame.CapturedAt).Text.Contains("psi") && !q.Answer("当前胎温多少",frame,frame.CapturedAt).HasVerifiedData);
        Require(!new TelemetryQueries().Answer("车速", frame,frame.CapturedAt).HasVerifiedData);
        var aliases = new Dictionary<string,string[]> { ["IR.Var.SpecialHybrid"]=["测试混动功率"] };
        var dynamicQueries=new TelemetryQueries(aliases,"队长",RacingGame.IRacing,frame.Native!.Catalog);
        Require(dynamicQueries.Answer("测试混动功率",frame,frame.CapturedAt).Text.Contains("123.25 kW"));
        Require(dynamicQueries.Answer("前车是谁",frame,frame.CapturedAt).Text.Contains("Rival"));
        Require(dynamicQueries.Answer("现在是什么旗",frame,frame.CapturedAt).Text.Contains("黄旗、蓝旗"));
        Require(frame.Reading("IR.Var.ExtraSamples")!.TextValue!.Contains("[1] -2.5"));
        Require(frame.Reading("IR.Var.Message")!.TextValue=="TEST");
        Require(frame.Reading("IR.Session.DriverInfo.Drivers.UserName")!.TextValue!.Contains("[0] Rival"));
        // New SDK revision with the same number and size of variables.
        Bytes(160+9*144+16,"AnotherSensor\0"); v.Write(12,2); v.Write(8200,12d);
        v.Write(8192+64, 0x20000); v.Write(8192+96, 4);
        var next=a.ReadMap(v,now.AddMilliseconds(200));
        Require(next.Traffic is { Racing: true } && next.Fields["gapAhead"].Value == 4500);
        Require(dynamicQueries.Answer("与前车差距", next, next.CapturedAt).Text.Contains("4.50 秒"));
        Require(frame.RaceControl is { Yellow: true, Penalty: 0 } && next.RaceControl is { Yellow: false, Penalty: 2 });
        Require(next.Reading("IR.Var.SpecialHybrid")==null && next.Reading("IR.Var.AnotherSensor")!=null);
        Require(frame.Reading("IR.Var.SpecialHybrid")!.Value==123.25);
        // Refresh UTF-8 session data and a time-subsample descriptor together.
        var utf8 = Encoding.UTF8.GetBytes(yaml.Replace("WeekendInfo:\n", "WeekendInfo:\n Encoding: UTF8\n")
            .Replace("Road America", "测试赛道").Replace("Rival", "张车手").Replace("Test GT3", "测试赛车"));
        v.WriteArray(12000,utf8,0,utf8.Length); v.Write(16,utf8.Length); v.Write(12,3);
        v.Write(160+13*144+12,(byte)1); v.Write(64,203); v.Write(8200,13d);
        Require(a.ReadMap(v,now.AddMilliseconds(300)).Phase == SessionPhase.Connecting);
        v.Write(64,204); v.Write(8200,14d);
        var refreshed = a.ReadMap(v,now.AddMilliseconds(400));
        Require(refreshed.Track == "测试赛道" && refreshed.CarModel == "测试赛车");
        Require(refreshed.Reading("IR.Session.DriverInfo.Drivers.UserName")!.TextValue!.Contains("张车手"));
        Require(refreshed.Reading("IR.Player.CarIdxPosition")!.Value == 2);
        Require(refreshed.Native!.Catalog.Single(f => f.Key == "IR.Var.ExtraSamples").Detail.Contains("时间子采样"));
        Require(frame.Track == "Road America" && frame.Reading("IR.Player.CarIdxPosition")!.Value == 2);
        v.Write(8194,(byte)1); Require(a.ReadMap(v,now).Phase == SessionPhase.Replay);
        v.Write(8194,(byte)0); v.Write(8192,(byte)0); Require(a.ReadMap(v,now).Phase == SessionPhase.Idle);
    }

    internal static void IRacingReferenceCatalog()
    {
        Require(IRacingReference.Variables.Count == 461 && IRacingReference.SessionPaths.Count == 259);
        var fields = RadioVocabulary.For(RacingGame.IRacing);
        Require(fields.Count == fields.Select(f => f.Key).Distinct().Count());
        Require(fields.Count == 797);
        foreach (var variable in IRacingReference.Variables)
            Require(fields.Any(f => f.Key == "IR.Var." + variable.Name));
        Require(!fields.Any(f => f.Key.EndsWith("UserID") || f.Key.EndsWith("TelemetryDiskFile")));
        var queries = new TelemetryQueries(new Dictionary<string,string[]>(), "工程师", RacingGame.IRacing);
        foreach (var field in fields)
            if (queries.FieldPhrases(field.Key).Length == 0)
                throw new InvalidOperationException("No query for " + field.Key);
        Require(queries.FieldPhrases("IR.Var.Precipitation").Contains("下雨吗"));
        Require(queries.FieldPhrases("IR.Var.EnergyERSBatteryPct").Contains("电池电量多少"));
        // A saved player-array alias survives a car/session which does not publish it.
        _ = new TelemetryQueries(new Dictionary<string,string[]> { ["IR.Player.CarIdxPosition"] = ["本车数组排名"] },
            "工程师", RacingGame.IRacing, []);
        var now = DateTimeOffset.UtcNow;
        var empty = RaceSnapshot.Empty(now, 0, SessionPhase.Live, "") with { Game = RacingGame.IRacing };
        Require(!queries.Answer("下雨吗", empty, now).HasVerifiedData);
    }

    internal static void IRacingNativeUnits()
    {
        var now = DateTimeOffset.UtcNow;
        var row = new byte[16];
        BitConverter.TryWriteBytes(row.AsSpan(), 180000f);
        var pa = new SdkVariable(4,0,1,"Pa","");
        var pressure = pa.Read(row,"IR.Var.dpLFTireColdPress",now)!;
        Require(pressure.Unit == "psi" && Math.Abs(pressure.Value!.Value - 26.1068) < .001);
        BitConverter.TryWriteBytes(row.AsSpan(), 180f);
        var kpa = pa with { Unit = "kPa" };
        Require(Math.Abs(kpa.Read(row,"IR.Var.LFcoldPressure",now)!.Value!.Value - pressure.Value!.Value) < .001);
        Require(kpa.Read(row,"IR.Var.OilPress",now)!.Unit == "kPa");
        BitConverter.TryWriteBytes(row.AsSpan(), .25f);
        Require((pa with { Unit = "%" }).Read(row,"IR.Var.Precipitation",now)!.Value == 25);
        BitConverter.TryWriteBytes(row.AsSpan(), 1); BitConverter.TryWriteBytes(row.AsSpan(4), 7);
        var array = new SdkVariable(2,0,2,"","");
        Require(array.Read(row,"IR.Player.CarIdxPosition",now,1)!.Value == 7);
        Require(array.Read(row,"IR.Player.CarIdxPosition",now,-1) == null && array.Read(row,"IR.Player.CarIdxPosition",now,2) == null);
        Require(IRacingSessionText.Decode(Encoding.UTF8.GetBytes("WeekendInfo:\n Encoding: UTF8\n TrackName: 中文\0ignored")).Contains("中文"));
        Require(IRacingSessionText.Decode(Encoding.Latin1.GetBytes("WeekendInfo:\n TrackName: Montréal\0ignored")).EndsWith("Montréal"));
    }

    private static void Require(bool condition, [System.Runtime.CompilerServices.CallerLineNumber] int line = 0)
    { if (!condition) throw new InvalidOperationException("Simulator fixture failed at line " + line); }
}
