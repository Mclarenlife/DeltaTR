namespace DeltaTR.Telemetry.ACC.SharedMemory;

/// <summary>
/// ACC 1.8.12 documented order, Windows wchar_t=2, pack=4.
/// Reserved fields retain their wire size and are excluded from the radio catalog.
/// </summary>
public sealed class AccLayout
{
    private readonly Dictionary<string, int> offsets = [];
    public int Size { get; private set; }
    public int this[string name] => offsets[name];
    private AccLayout Add(string name, int bytes, int alignment = 4)
    {
        Size = (Size + alignment - 1) / alignment * alignment;
        offsets.Add(name, Size);
        Size += bytes;
        return this;
    }
    private AccLayout Scalars(params string[] names)
    {
        foreach (var name in names) Add(name, 4);
        return this;
    }
    private AccLayout Text(string name, int chars) => Add(name, chars * 2, 2);

    public static AccLayout Physics { get; } = new AccLayout()
        .Scalars("packetId", "gas", "brake", "fuel", "gear", "rpm", "steerAngle", "speedKmh")
        .Add("velocity", 12).Add("accG", 12)
        .Add("wheelSlip", 16).Add("wheelLoad", 16).Add("wheelPressure", 16)
        .Add("wheelAngularSpeed", 16).Add("tyreWear_UNUSED", 16).Add("tyreDirtyLevel", 16)
        .Add("tyreCoreTemperature", 16).Add("camberRAD", 16).Add("suspensionTravel", 16)
        .Scalars("drs", "tc", "heading", "pitch", "roll", "cgHeight")
        .Add("carDamage", 20)
        .Scalars("numberOfTyresOut", "pitLimiterOn", "abs", "kersCharge", "kersInput", "autoshifterOn")
        .Add("rideHeight", 8).Scalars("turboBoost", "ballast", "airDensity", "airTemp", "roadTemp")
        .Add("localAngularVel", 12).Scalars("finalFF", "performanceMeter", "engineBrake", "ersRecoveryLevel",
            "ersPowerLevel", "ersHeatCharging", "ersIsCharging", "kersCurrentKJ", "drsAvailable", "drsEnabled")
        .Add("brakeTemp", 16).Scalars("clutch").Add("tyreTempI", 16).Add("tyreTempM", 16).Add("tyreTempO", 16)
        .Scalars("isAIControlled").Add("tyreContactPoint", 48).Add("tyreContactNormal", 48).Add("tyreContactHeading", 48)
        .Scalars("brakeBias").Add("localVelocity", 12).Scalars("p2pActivation", "p2pStatus", "currentMaxRpm")
        .Add("mz", 16).Add("fx", 16).Add("fy", 16).Add("slipRatio", 16).Add("slipAngle", 16)
        .Scalars("tcInAction", "absInAction").Add("suspensionDamage", 16).Add("tyreTemp", 16)
        .Scalars("waterTemp").Add("brakePressure", 16).Scalars("frontBrakeCompound", "rearBrakeCompound")
        .Add("padLife", 16).Add("discLife", 16).Scalars("ignitionOn", "starterEngineOn", "isEngineRunning",
            "kerbVibration", "slipVibrations", "gVibrations", "absVibrations");

    public static AccLayout Graphics { get; } = new AccLayout()
        .Scalars("packetId", "status", "session")
        .Text("currentTime", 15).Text("lastTime", 15).Text("bestTime", 15).Text("split", 15)
        .Scalars("completedLaps", "position", "iCurrentTime", "iLastTime", "iBestTime", "sessionTimeLeft",
            "distanceTraveled", "isInPit", "currentSectorIndex", "lastSectorTime", "numberOfLaps")
        .Text("tyreCompound", 33).Scalars("replayTimeMultiplier", "normalizedCarPosition", "activeCars")
        .Add("carCoordinates", 720).Add("carID", 240)
        .Scalars("playerCarID", "penaltyTime", "flag", "penalty", "idealLineOn", "isInPitLane",
            "surfaceGrip", "mandatoryPitDone", "windSpeed", "windDirection", "isSetupMenuVisible",
            "mainDisplayIndex", "secondaryDisplayIndex", "TC", "TCCUT", "EngineMap", "ABS", "fuelXLap",
            "rainLights", "flashingLights", "lightsStage", "exhaustTemperature", "wiperLV",
            "driverStintTotalTimeLeft", "driverStintTimeLeft", "rainTyres", "sessionIndex", "usedFuel")
        .Text("deltaLapTime", 15).Scalars("iDeltaLapTime")
        .Text("estimatedLapTime", 15).Scalars("iEstimatedLapTime", "isDeltaPositive", "iSplit", "isValidLap", "fuelEstimatedLaps")
        .Text("trackStatus", 33)
        .Scalars("missingMandatoryPits", "clock", "directionLightsLeft", "directionLightsRight", "globalYellow",
            "globalYellow1", "globalYellow2", "globalYellow3", "globalWhite", "globalGreen", "globalChequered",
            "globalRed", "mfdTyreSet", "mfdFuelToAdd", "mfdTyrePressureLF", "mfdTyrePressureRF",
            "mfdTyrePressureLR", "mfdTyrePressureRR", "trackGripStatus", "rainIntensity",
            "rainIntensityIn10min", "rainIntensityIn30min", "currentTyreSet", "strategyTyreSet", "gapAhead", "gapBehind");

    public static AccLayout Static { get; } = new AccLayout()
        .Text("smVersion", 15).Text("acVersion", 15).Scalars("numberOfSessions", "numCars")
        .Text("carModel", 33).Text("track", 33).Text("playerName", 33)
        .Text("playerSurname", 33).Text("playerNick", 33)
        .Scalars("sectorCount", "maxTorque", "maxPower", "maxRpm", "maxFuel")
        .Add("suspensionMaxTravel", 16).Add("tyreRadius", 16).Scalars("maxTurboBoost", "deprecated1", "deprecated2",
            "penaltiesEnabled", "aidFuelRate", "aidTireRate", "aidMechanicalDamage", "allowTyreBlankets",
            "aidStability", "aidAutoclutch", "aidAutoBlip", "hasDRS", "hasERS", "hasKERS", "kersMaxJ",
            "engineBrakeSettingsCount", "ersPowerControllerCount", "trackSplineLength")
        .Text("trackConfiguration", 33).Scalars("ersMaxJ", "isTimedRace", "hasExtraLap")
        .Text("carSkin", 33).Scalars("reversedGridPositions", "PitWindowStart", "PitWindowEnd", "isOnline")
        .Text("dryTyresName", 33).Text("wetTyresName", 33);
}
