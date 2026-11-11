namespace DeltaTR.Core;

public static class IRacingChannels
{
    // Reference list for offline browsing. The connected SDK descriptor table takes precedence.
    private const string Reference = """
        AirDensity AirPressure AirTemp Brake BrakeABSactive BrakeRaw CFshockDefl
        CFshockVel CRshockDefl CRshockVel CamCameraNumber CamCameraState CamCarIdx CamGroupNumber
        CarIdxBestLapNum CarIdxBestLapTime CarIdxClass CarIdxClassPosition CarIdxEstTime CarIdxF2Time CarIdxFastRepairsUsed
        CarIdxGear CarIdxLap CarIdxLapCompleted CarIdxLapDistPct CarIdxLastLapTime CarIdxOnPitRoad CarIdxP2P_Count
        CarIdxP2P_Status CarIdxPaceFlags CarIdxPaceLine CarIdxPaceRow CarIdxPosition CarIdxQualTireCompound CarIdxQualTireCompoundLocked
        CarIdxRPM CarIdxSteer CarIdxTireCompound CarIdxTrackSurface CarIdxTrackSurfaceMaterial CarLeftRight ChanAvgLatency
        ChanClockSkew ChanLatency ChanPartnerQuality ChanQuality Clutch CpuUsageBG CpuUsageFG
        DCDriversSoFar DCLapStatus DisplayUnits DriverMarker EngineWarnings EnterExitReset FastRepairAvailable
        FastRepairUsed FogLevel FrameRate FrontTireSetsAvailable FrontTireSetsUsed FuelLevel FuelLevelPct
        FuelPress FuelUsePerHour Gear GpuUsage HandbrakeRaw IsDiskLoggingActive IsDiskLoggingEnabled
        IsInGarage IsOnTrack IsOnTrackCar IsReplayPlaying LFTiresAvailable LFTiresUsed LFcoldPressure
        LFshockDefl LFshockDefl_ST LFshockVel LFshockVel_ST LFtempCL LFtempCM LFtempCR
        LFwearL LFwearM LFwearR LRTiresAvailable LRTiresUsed LRcoldPressure LRshockDefl
        LRshockDefl_ST LRshockVel LRshockVel_ST LRtempCL LRtempCM LRtempCR LRwearL
        LRwearM LRwearR Lap LapBestLap LapBestLapTime LapBestNLapLap LapBestNLapTime
        LapCompleted LapCurrentLapTime LapDeltaToBestLap LapDeltaToBestLap_DD LapDeltaToBestLap_OK LapDeltaToOptimalLap LapDeltaToOptimalLap_DD
        LapDeltaToOptimalLap_OK LapDeltaToSessionBestLap LapDeltaToSessionBestLap_DD LapDeltaToSessionBestLap_OK LapDeltaToSessionLastlLap LapDeltaToSessionLastlLap_DD LapDeltaToSessionLastlLap_OK
        LapDeltaToSessionOptimalLap LapDeltaToSessionOptimalLap_DD LapDeltaToSessionOptimalLap_OK LapDist LapDistPct LapLasNLapSeq LapLastLapTime
        LapLastNLapTime Lat LatAccel LatAccel_ST LeftTireSetsAvailable LeftTireSetsUsed LoadNumTextures
        Lon LongAccel LongAccel_ST ManifoldPress ManualBoost ManualNoBoost MemPageFaultSec
        OilLevel OilPress OilTemp OkToReloadTextures OnPitRoad PaceMode PitOptRepairLeft
        PitRepairLeft PitSvFlags PitSvFuel PitSvLFP PitSvLRP PitSvRFP PitSvRRP
        PitSvTireCompound Pitch PitchRate PitchRate_ST PitsOpen PitstopActive PlayerCarClass
        PlayerCarClassPosition PlayerCarDriverIncidentCount PlayerCarDryTireSetLimit PlayerCarIdx PlayerCarInPitStall PlayerCarMyIncidentCount PlayerCarPitSvStatus
        PlayerCarPosition PlayerCarPowerAdjust PlayerCarTeamIncidentCount PlayerCarTowTime PlayerCarWeightPenalty PlayerFastRepairsUsed PlayerTireCompound
        PlayerTrackSurface PlayerTrackSurfaceMaterial PushToPass RFTiresAvailable RFTiresUsed RFcoldPressure RFshockDefl
        RFshockDefl_ST RFshockVel RFshockVel_ST RFtempCL RFtempCM RFtempCR RFwearL
        RFwearM RFwearR RPM RRTiresAvailable RRTiresUsed RRcoldPressure RRshockDefl
        RRshockDefl_ST RRshockVel RRshockVel_ST RRtempCL RRtempCM RRtempCR RRwearL
        RRwearM RRwearR RaceLaps RadioTransmitCarIdx RadioTransmitFrequencyIdx RadioTransmitRadioIdx RearTireSetsAvailable
        RearTireSetsUsed RelativeHumidity ReplayFrameNum ReplayFrameNumEnd ReplayPlaySlowMotion ReplayPlaySpeed ReplaySessionNum
        ReplaySessionTime RightTireSetsAvailable RightTireSetsUsed Roll RollRate RollRate_ST SessionFlags
        SessionJokerLapsRemain SessionLapsRemain SessionLapsRemainEx SessionLapsTotal SessionNum SessionOnJokerLap SessionState
        SessionTick SessionTime SessionTimeOfDay SessionTimeRemain SessionTimeTotal SessionUniqueID ShiftGrindRPM
        ShiftIndicatorPct ShiftPowerPct Skies Speed SteeringWheelAngle SteeringWheelAngleMax SteeringWheelLimiter
        SteeringWheelMaxForceNm SteeringWheelPctDamper SteeringWheelPctTorque SteeringWheelPctTorqueSign SteeringWheelPctTorqueSignStops SteeringWheelPeakForceNm SteeringWheelTorque
        SteeringWheelTorque_ST SteeringWheelUseLinear Throttle ThrottleRaw TireLF_RumblePitch TireLR_RumblePitch TireRF_RumblePitch
        TireRR_RumblePitch TireSetsAvailable TireSetsUsed TrackTemp TrackTempCrew TrackWetness VelocityX
        VelocityX_ST VelocityY VelocityY_ST VelocityZ VelocityZ_ST VertAccel VertAccel_ST
        VidCapActive VidCapEnabled Voltage WaterLevel WaterTemp WeatherType WindDir
        WindVel Yaw YawNorth YawRate YawRate_ST dcABS dcAntiRollFront
        dcAntiRollRear dcBoostLevel dcBrakeBias dcDiffEntry dcDiffExit dcDiffMiddle dcEngineBraking
        dcEnginePower dcFuelMixture dcRevLimiter dcStarter dcTearOffVisor dcThrottleShape dcTractionControl
        dcTractionControl2 dcTractionControlToggle dcWeightJackerLeft dcWeightJackerRight dcWingFront dcWingRear dpFNOMKnobSetting
        dpFUFangleIndex dpFWingAngle dpFWingIndex dpFastRepair dpFuelAddKg dpFuelFill dpLFTireChange
        dpLFTireColdPress dpLRTireChange dpLRTireColdPress dpLrWedgeAdj dpPSSetting dpQtape dpRBarSetting
        dpRFTireChange dpRFTireColdPress dpRFTruckarmP1Dz dpRRDamperPerchOffsetm dpRRTireChange dpRRTireColdPress dpRWingAngle
        dpRWingIndex dpRWingSetting dpRrPerchOffsetm dpRrWedgeAdj dpTruckarmP1Dz dpWedgeAdj
        """;

    private static string ChannelTitle(string name)
    {
        foreach (var (prefix, corner) in new[] { ("LF","左前"), ("RF","右前"), ("LR","左后"), ("RR","右后") })
        {
            if (!name.StartsWith(prefix,StringComparison.Ordinal)) continue;
            var meaning = name[prefix.Length..] switch
            {
                "coldPressure" => "冷胎压设定", "pressure" => "压力原始通道",
                "tempCL" => "最近左侧胎体温度", "tempCM" => "最近中部胎体温度", "tempCR" => "最近右侧胎体温度",
                "wearL" => "左侧胎面剩余比例", "wearM" => "中部胎面剩余比例", "wearR" => "右侧胎面剩余比例",
                "shockDefl" => "减震器行程", "shockVel" => "减震器速度", "shockDefl_ST" => "减震器行程子采样",
                "shockVel_ST" => "减震器速度子采样", "TiresUsed" => "已用轮胎数", "TiresAvailable" => "可用轮胎数", _ => null
            };
            if (meaning!=null) return "原始" + corner + meaning;
        }
        return "遥测" + Spoken(name);
    }
    public static NativeField Variable(string name)
    {
        var title = IRacingLabels.Find(name) ?? (name switch
        {
            "SessionFlags" => "赛段旗语标志位", "CarIdxPosition" => "各车总排名", "CarIdxClassPosition" => "各车组别排名",
            "CarIdxLapCompleted" => "各车已完成圈数", "CarIdxLapDistPct" => "各车单圈进度", "CarIdxOnPitRoad" => "各车维修区状态",
            "CarIdxBestLapTime" => "各车最佳圈速", "CarIdxLastLapTime" => "各车上一圈用时", "CarIdxF2Time" => "各车计时板时间",
            "Throttle" => "油门开度", "Brake" => "制动踏板开度", "Clutch" => "离合开度", "FuelUsePerHour" => "每小时燃油消耗",
            "OilTemp" => "发动机油温", "OilPress" => "机油压力", "WindVel" => "风速", "WindDir" => "风向",
            "Precipitation" => "降水强度", "TrackWetness" => "赛道路面湿度等级", "PlayerCarClassPosition" => "本车组别排名",
            "LapBestLapTime" => "最佳圈用时", "SessionLapsRemainEx" => "赛段剩余圈数", "PitRepairLeft" => "剩余维修时间",
            _ => ChannelTitle(name)
        });
        var detail = IRacingReference.ByName.TryGetValue(name, out var info)
            ? IRacingReference.Metadata(info.Type, info.Count, info.CountAsTime, info.Unit, info.Description, false)
            : "旧版或车型专用参考变量，是否提供以当前会话为准。";
        return new("IR.Var." + name, title, IRacingLabels.Group(name), detail + ReadingNote(name));
    }

    public static string Spoken(string value) => string.Concat(value.Where(char.IsLetterOrDigit));
    public static NativeField Variable(string name, int type, int count, bool timeSamples, string unit, string description) =>
        Variable(name) with { Detail = IRacingReference.Metadata(type, count, timeSamples, unit, description, true) + ReadingNote(name) };

    private static string ReadingNote(string name) =>
        name.Contains("tempC", StringComparison.Ordinal) || name.Contains("wear", StringComparison.Ordinal)
            ? " 轮胎报告为上次维修区测量，不代表当前行驶中的温度或磨损。"
        : name.EndsWith("coldPressure", StringComparison.Ordinal) || name.EndsWith("TireColdPress", StringComparison.Ordinal)
            ? " 冷胎压或进站设定，不是当前热胎压；显示统一为 psi。"
        : name.EndsWith("pressure", StringComparison.Ordinal) || name is "LFtempL" or "LFtempM" or "LFtempR" or
            "RFtempL" or "RFtempM" or "RFtempR" or "LRtempL" or "LRtempM" or "LRtempR" or "RRtempL" or "RRtempM" or "RRtempR"
            ? " 参考库包含磁盘遥测通道；仅当实时描述表公布时读取，不承诺所有车型实时可用。" : "";

    public static NativeField PlayerField(string name) =>
        new("IR.Player." + name, "本车数组" + Variable(name).Title.Replace("各车", "", StringComparison.Ordinal),
            "本车数组读数", "从 " + name + " 按本车 CarIdx 读取；不是数组中的第一辆车。" + ReadingNote(name));

    public static NativeField SessionField(string path) =>
        new("IR.Session." + path, "会话" + Spoken(path), "SDK 会话资料", "会话 YAML · " + path +
            "。随游戏会话资料更新；序号为列表位置，不等于 CarIdx。不是物理帧的实时采样。");

    public static readonly IReadOnlyList<NativeField> ReferenceFields = Reference.Split((char[]?)null,
        StringSplitOptions.RemoveEmptyEntries).Concat(IRacingReference.Variables.Select(v => v.Name))
        .Distinct(StringComparer.Ordinal).Select(Variable).Concat(SimulatorRace.Fields)
        .Concat(IRacingReference.Variables.Where(v => v.Name.StartsWith("CarIdx", StringComparison.Ordinal)).Select(v => PlayerField(v.Name)))
        .Concat(IRacingReference.SessionPaths.Where(p => p.Path.Split('.').All(IRacingReference.IncludeSessionField))
            .Select(p => SessionField(p.Path))).ToArray();
}
