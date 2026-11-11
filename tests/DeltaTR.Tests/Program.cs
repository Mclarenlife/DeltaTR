using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.IO.MemoryMappedFiles;
using DeltaTR.Core;
using DeltaTR.Engineer;
using DeltaTR.Storage;
using DeltaTR.Telemetry.ACC;
using DeltaTR.Telemetry.ACC.Mapping;
using DeltaTR.Telemetry.ACC.SharedMemory;

// Dependency-free regression runner. Nonzero process exit on any failed case; no NuGet needed.
var cases = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action run) => cases.Add((name, () => { run(); return Task.CompletedTask; }));
void AsyncTest(string name, Func<Task> run) => cases.Add((name, run));
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; actual {actual}");
void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}");
}
var now = new DateTimeOffset(2026, 11, 3, 5, 0, 0, TimeSpan.Zero);

Test("MOZA / captured KS Pro stream marker, catalog, CRC and UTF-8 boundaries", MozaDisplaySession.SelfTest);
AsyncTest("MOZA / dedicated field, legacy client rejection, acknowledgements, stale messages and restart", MozaBridgeTests.Loopback);
Test("MOZA / dedicated field installation, V1 migration, tamper refusal and interrupted recovery", MozaInstallationTests.Recovery);
Test("MOZA / short press, hold threshold, release, disconnect and held-button suppression", ScreenInteractionTests.Gestures);
Test("MOZA / manual pinning, temporary alerts, return and dismissal", ScreenInteractionTests.Presentation);
Test("MOZA / long Chinese answers preserve characters across bounded pages", () =>
{
    var answer = string.Concat(Enumerable.Repeat("左前胎温86度，建议保持节奏🏁。", 12));
    var message = new DeltaTR.Hardware.Moza.WheelMessage("reply", answer);
    var restored = new StringBuilder();
    for (int page = 0; restored.Length < answer.Length; page++)
    {
        Check(page < 20);
        var rows = DeltaTR.Hardware.Moza.WheelScreen.EncodeRows(message, TimeSpan.FromSeconds(page * 8));
        Equal(5, rows.Length);
        foreach (var row in rows)
        {
            Check(Encoding.UTF8.GetByteCount(row) <= 127);
            var parts = row.Split('|', 3);
            Check(parts[0] == "reply" && int.Parse(parts[1]) is >= 0 and <= 204);
            restored.Append(parts[2]);
        }
    }
    Equal(answer, restored.ToString());
    var sample = new string('胎', 120);
    Equal(sample, string.Concat(DeltaTR.Hardware.Moza.WheelScreen.EncodeRows(new("reply", sample), TimeSpan.Zero).Select(r => r.Split('|', 3)[2])));
    Equal("offline|102|未连接游戏", DeltaTR.Hardware.Moza.WheelScreen.EncodeRows(new("offline", "未连接游戏"), TimeSpan.Zero)[0]);
});

// Golden bytes are deliberately written at literal offsets, not offsets from the decoder under test.
(byte[] P, byte[] G, byte[] S) Bytes()
{
    var p = new byte[800]; var g = new byte[1588]; var s = new byte[820];
    void I(byte[] b, int at, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at, 4), v);
    void F(byte[] b, int at, float v) => I(b, at, BitConverter.SingleToInt32Bits(v));
    void T(byte[] b, int at, string v) => Encoding.Unicode.GetBytes(v).CopyTo(b, at);
    I(p, 0, 100); F(p, 12, 44.5f); I(p, 16, 4); I(p, 20, 6500); F(p, 28, 123.5f);
    for (var i = 0; i < 4; i++) { F(p, 88 + i * 4, 26.1f + i); F(p, 152 + i * 4, 81 + i); F(p, 120 + i * 4, 0.123f); }
    F(p, 288, 24); F(p, 292, 32);
    I(g, 0, 200); I(g, 4, 2); I(g, 8, 2); I(g, 132, 4); I(g, 136, 5); I(g, 144, 107200);
    I(g, 1316, 1); I(g, 1484, 1); // rainTyres and missingMandatoryPits
    I(g, 1556, 2); I(g, 1560, 0); I(g, 1564, 1); I(g, 1568, 2);
    I(g, 1580, 1840); I(g, 1584, 2300);
    T(s, 0, "1.8.12"); T(s, 30, "1.9.8"); T(s, 68, "bmw_m4_gt3"); T(s, 134, "monza"); F(s, 416, 120);
    return (p, g, s);
}
AccRawFrame Raw()
{
    var (p, g, s) = Bytes();
    return AccDecoder.Decode(p, g, s);
}
CalibrationProfile Profile() => new()
{
    GameVersion = "1.9.8", SharedMemoryVersion = "1.8.12", CarModel = "bmw_m4_gt3", Track = "monza", Evidence = "synthetic test evidence ONLY",
    VerifiedUnits = new() { ["fuel"] = "L", ["speed"] = "km/h", ["airTemperature"] = "°C", ["gapAhead"] = "ms", ["gameFuelEstimatedLaps"] = "lap" }
};

Test("Protocol / documented prefix sizes", () =>
{
    Equal(800, AccLayout.Physics.Size); Equal(1588, AccLayout.Graphics.Size); Equal(820, AccLayout.Static.Size);
});
Test("Protocol / graphics UTF16 alignment and tail offsets", () =>
{
    Equal(132, AccLayout.Graphics["completedLaps"]); Equal(244, AccLayout.Graphics["replayTimeMultiplier"]);
    Equal(1580, AccLayout.Graphics["gapAhead"]); Equal(1584, AccLayout.Graphics["gapBehind"]);
});
Test("Protocol / golden bytes and wheel order", () =>
{
    var raw = Raw(); Equal(44.5, raw.Values["fuel"].Value); Equal(123.5, raw.Values["speed"].Value);
    Equal(24d, raw.Values["airTemperature"].Value); Equal(32d, raw.Values["roadTemperature"].Value);
    Equal(81d, raw.Values["temperature.FL"].Value); Equal(84d, raw.Values["temperature.RR"].Value);
    Equal("bmw_m4_gt3", raw.CarModel); Equal("monza", raw.Track); Equal(1840d, raw.Values["gapAhead"].Value);
    Equal(2d, raw.Values["trackGrip"].Value); Equal(1d, raw.Values["rainIn10Minutes"].Value);
    Equal(1d, raw.Values["missingMandatoryPits"].Value); Equal(1d, raw.Values["rainTyres"].Value);
});
Test("Live tyre radio / repeated questions follow physics packets independently of graphics", () =>
{
    var (physics, graphics, info) = Bytes();
    var normalizer = new AccNormalizer(new());
    var radio = new TelemetryQueries();
    RaceSnapshot Sample(int packet, int graphicsPacket, long elapsed)
    {
        BinaryPrimitives.WriteInt32LittleEndian(physics, packet);
        BinaryPrimitives.WriteInt32LittleEndian(graphics, graphicsPacket);
        return normalizer.Normalize(AccDecoder.Decode(physics, graphics, info), now.AddMilliseconds(elapsed), elapsed);
    }

    Sample(100, 200, 0);
    var first = Sample(101, 201, 50);
    for (var i = 0; i < 4; i++)
    {
        BinaryPrimitives.WriteSingleLittleEndian(physics.AsSpan(88 + i * 4), 27 + i * 0.25f);
        BinaryPrimitives.WriteSingleLittleEndian(physics.AsSpan(152 + i * 4), 90 + i);
    }
    var second = Sample(102, 201, 100);
    foreach (var imperial in new[] { false, true })
    {
        var a = DisplayUnits.ForDisplay(first, imperial);
        var b = DisplayUnits.ForDisplay(second, imperial);
        foreach (var question in new[] { "胎压", "胎温", "左前胎压", "右后胎温" })
        {
            var old = radio.Answer(question, a, first.CapturedAt);
            var latest = radio.Answer(question, b, second.CapturedAt);
            Check(old.HasVerifiedData && latest.HasVerifiedData && latest.Text != old.Text);
        }
        if (imperial) Check(radio.Answer("胎温", b, second.CapturedAt).Text.Contains("194.0、195.8、197.6、199.4 °F"));
        Equal(radio.Answer("胎压", second, second.CapturedAt).Text, radio.Answer("胎压", b, second.CapturedAt).Text);
    }
    var frozen = Sample(102, 202, 3000);
    foreach (var question in new[] { "胎压", "胎温" })
        Check(!radio.Answer(question, frozen, frozen.CapturedAt).HasVerifiedData);
    var resumed = Sample(103, 203, 3050);
    Check(radio.Answer("胎温", resumed, resumed.CapturedAt).Text.Contains("90.0、91.0、92.0、93.0 °C"));
});
Test("Protocol / truncated pages rejected", () => Throws<InvalidDataException>(() => AccDecoder.Decode(new byte[295], new byte[1588], new byte[420])));
Test("Protocol / tyre wear is never exposed", () => Check(!Raw().Values.Keys.Any(x => x.Contains("wear", StringComparison.OrdinalIgnoreCase))));
Test("Protocol / Windows named map read-only reader", () =>
{
    var name = @"Local\DeltaTR_test_" + Guid.NewGuid().ToString("N");
    var bytes = Bytes().P;
    using var memory = MemoryMappedFile.CreateNew(name, bytes.Length);
    using var writer = memory.CreateViewAccessor();
    writer.WriteArray(0, bytes, 0, bytes.Length);
    using var reader = new ReadOnlyPage(name, bytes.Length);
    Check(bytes.SequenceEqual(reader.Read(true)));
});
Test("Protocol / UTF16 metadata supports non-ASCII", () =>
{
    var (p, g, s) = Bytes(); Array.Clear(s, 68, 66);
    Encoding.Unicode.GetBytes("测试车型").CopyTo(s, 68);
    Equal("测试车型", AccDecoder.Decode(p, g, s).CarModel);
});
Test("Quality / unknown layout never assumes fuel units", () =>
{
    var state = new AccNormalizer(new()).Normalize(Raw() with { SharedMemoryVersion = "unknown" }, now, 0);
    Equal(DataQuality.NeedsVerification, state.Fields["fuel"].Quality); Equal("raw", state.Fields["fuel"].Unit);
});
Test("Quality / exact profile admits verified fields only", () =>
{
    var state = new AccNormalizer(Profile()).Normalize(Raw(), now, 0);
    Equal(DataQuality.Valid, state.Fields["fuel"].Quality); Equal("L", state.Fields["fuel"].Unit);
    Equal(DataQuality.Unavailable, state.Fields["temperature.FL"].Quality);
});
Test("Quality / mismatched game version invalidates calibration", () =>
{
    var state = new AccNormalizer(Profile()).Normalize(Raw() with { GameVersion = "1.10" }, now, 0);
    Check(!state.Fields["fuel"].IsUsable(now, Metrics.MaxAge));
});
Test("Quality / mismatched car invalidates calibration", () =>
{
    var state = new AccNormalizer(Profile()).Normalize(Raw() with { CarModel = "other" }, now, 0);
    Check(!state.Fields["fuel"].IsUsable(now, Metrics.MaxAge));
});
Test("Quality / empty evidence does not approve a first-frame reading", () => Equal(DataQuality.Unavailable,
    new AccNormalizer(Profile() with { Evidence = "" }).Normalize(Raw(), now, 0).Fields["fuel"].Quality));
Test("Quality / incorrect calibration units are rejected", () =>
{
    var profile = Profile(); profile.VerifiedUnits["speed"] = "mph";
    var reading = new AccNormalizer(profile).Normalize(Raw(), now, 0).Fields["speed"];
    Equal("km/h", reading.Unit); Check(!reading.IsUsable(now, Metrics.MaxAge));
});
Test("Radio / direct readings need fresh live updates, not HUD input", () =>
{
    var reader = new AccNormalizer(new());
    var raw = Raw() with { SharedMemoryVersion = "1.9", CarModel = "ford_mustang_gt3", Track = "Spa" };
    var queries = new TelemetryQueries();
    var first = reader.Normalize(raw, now, 0);
    Check(!queries.Answer("排名", first, now).HasVerifiedData);
    var driving = raw with { GraphicsPacketId = 201, PhysicsPacketId = 101 };
    var next = reader.Normalize(driving, now.AddMilliseconds(50), 50);
    var rank = queries.Answer("排名", next, next.CapturedAt);
    Check(rank.HasVerifiedData && rank.Text.Contains("第 5 名"));
    foreach (var key in new[] { "speed", "completedLaps", "lastLapTime", "gapBehind" })
        Equal(DataQuality.Valid, next.Fields[key].Quality);
    Equal(DataQuality.Valid, next.Fields["fuel"].Quality);
    Equal("L", next.Fields["fuel"].Unit);
    Equal(DataQuality.Unavailable, next.Fields["sessionTimeLeft"].Quality);
    foreach (var key in new[] { "airTemperature", "roadTemperature", "pressure.FL", "temperature.RR" })
        Equal(DataQuality.Valid, next.Fields[key].Quality);
    Equal(DataQuality.Stale, reader.Normalize(driving, now.AddSeconds(3), 3000).Fields["position"].Quality);
    Equal(DataQuality.Unavailable, reader.Normalize(driving with { Status = 3 }, now.AddSeconds(4), 4000).Fields["position"].Quality);
    Equal(DataQuality.Stale, reader.Normalize(driving, now.AddSeconds(5), 5000).Fields["position"].Quality);
    driving.Values["position"] = driving.Values["position"] with { Value = 0 };
    Equal(DataQuality.Invalid, reader.Normalize(driving with { GraphicsPacketId = 202 }, now.AddSeconds(6), 6000).Fields["position"].Quality);
});
Test("Units / display conversion preserves native data, quality and time", () =>
{
    var s = SyntheticAdapter.Create(0, 0, now);
    var metric = DisplayUnits.ForDisplay(s, false)!;
    var imperial = DisplayUnits.ForDisplay(s, true)!;
    var psi = s.Fields["pressure.FL"];
    Equal("psi", psi.Unit); Equal(psi, metric.Fields["pressure.FL"]);
    Equal(psi, imperial.Fields["pressure.FL"]);
    Equal(psi.ObservedAt, metric.Fields["pressure.FL"].ObservedAt);
    Equal("°F", imperial.Fields["temperature.FL"].Unit);
    Check(new TelemetryQueries().Answer("气温", imperial, now).Text.Contains("75.2 °F"));
    Check(new TelemetryQueries().Answer("气温", metric, now).Text.Contains("24.0 °C"));
    Check(new TelemetryQueries().Answer("车辆状况", imperial, now).Text.Contains("imp gal"));
    Check(new TelemetryQueries().Answer("车速", imperial, now).Text.Contains("mph"));
    s.Fields["pressure.FL"] = psi with { Quality = DataQuality.Stale };
    Check(!new TelemetryQueries().Answer("胎压", DisplayUnits.ForDisplay(s, false), now).HasVerifiedData);
});
foreach (var (initial, later, unit) in new[] { (120d, 118d, "s"), (120000d, 118000d, "ms") })
Test("Units / live countdown identifies " + unit, () =>
{
    var reader = new AccNormalizer(new());
    var raw = Raw();
    var field = raw.Values["sessionTimeLeft"];
    raw.Values["sessionTimeLeft"] = field with { Value = initial };
    reader.Normalize(raw, now, 0);
    raw.Values["sessionTimeLeft"] = field with { Value = later };
    var s = reader.Normalize(raw with { GraphicsPacketId = 201 }, now.AddSeconds(2), 2000);
    Equal(unit, s.Fields["sessionTimeLeft"].Unit);
    Equal(DataQuality.Valid, s.Fields["sessionTimeLeft"].Quality);
});
Test("Radio / unknown layouts do not automatically enable position", () =>
{
    var reader = new AccNormalizer(new());
    var raw = Raw() with { SharedMemoryVersion = "unknown" };
    reader.Normalize(raw, now, 0);
    Equal(DataQuality.NeedsVerification, reader.Normalize(raw with { GraphicsPacketId = 201 }, now.AddMilliseconds(50), 50).Fields["position"].Quality);
});
Test("Quality / duplicate packets expire independently", () =>
{
    var map = new AccNormalizer(Profile()); map.Normalize(Raw(), now, 0);
    var state = map.Normalize(Raw() with { GraphicsPacketId = 201 }, now.AddSeconds(3), 3000);
    Equal(DataQuality.Stale, state.Fields["fuel"].Quality); Equal(DataQuality.Valid, state.Fields["gapAhead"].Quality);
    Equal(now, state.Fields["fuel"].ObservedAt); Equal(now.AddSeconds(3), state.Fields["gapAhead"].ObservedAt);
});
Test("Quality / stale uses monotonic time even if wall clock changes", () =>
{
    var map = new AccNormalizer(Profile()); map.Normalize(Raw(), now, 0);
    Equal(DataQuality.Stale, map.Normalize(Raw(), now.AddHours(-1), 3000).Fields["fuel"].Quality);
});
Test("Quality / NaN never becomes valid or breaks JSON", () =>
{
    var raw = Raw(); raw.Values["fuel"] = raw.Values["fuel"] with { Value = double.NaN };
    var state = new AccNormalizer(Profile()).Normalize(raw, now, 0);
    Equal(DataQuality.Invalid, state.Fields["fuel"].Quality); Check(state.Fields["fuel"].Value is null);
    JsonSerializer.Serialize(state, TelemetryJson.Options);
});
Test("Quality / zero pressure is invalid", () =>
{
    var raw = Raw(); raw.Values["pressure.FL"] = raw.Values["pressure.FL"] with { Value = 0 };
    Equal(DataQuality.Invalid, new AccNormalizer(new()).Normalize(raw, now, 0).Fields["pressure.FL"].Quality);
});
Test("Quality / game fuel estimate stays estimated", () =>
{
    var raw = Raw(); raw.Values["gameFuelEstimatedLaps"] = raw.Values["gameFuelEstimatedLaps"] with { Value = 10 };
    Equal(DataQuality.Estimated, new AccNormalizer(Profile()).Normalize(raw, now, 0).Fields["gameFuelEstimatedLaps"].Quality);
});
foreach (var status in new[] { 0, 1, 3, 99 })
Test($"Session / non-live status {status} blocks queries", () =>
{
    var s = new AccNormalizer(Profile()).Normalize(Raw() with { Status = status }, now, 0);
    Check(!new TelemetryQueries().Answer("油量", s, now).HasVerifiedData);
});
Test("Session / lap rollback starts new session", () =>
{
    var map = new AccNormalizer(Profile()); var first = map.Normalize(Raw(), now, 0);
    var second = map.Normalize(Raw() with { CompletedLaps = 0 }, now.AddSeconds(1), 1000);
    Check(first.SessionId != second.SessionId);
});
Test("Session / pause and resume preserve session", () =>
{
    var map = new AccNormalizer(Profile()); var first = map.Normalize(Raw(), now, 0);
    map.Normalize(Raw() with { Status = 3 }, now.AddSeconds(1), 1000);
    Equal(first.SessionId, map.Normalize(Raw() with { PhysicsPacketId = 101 }, now.AddSeconds(2), 2000).SessionId);
});
Test("Session / idle to live resets session", () =>
{
    var map = new AccNormalizer(Profile()); var first = map.Normalize(Raw(), now, 0);
    map.Normalize(Raw() with { Status = 0 }, now.AddSeconds(1), 1000);
    Check(first.SessionId != map.Normalize(Raw(), now.AddSeconds(2), 2000).SessionId);
});
Test("Session / process disconnect resets identity", () =>
{
    var map = new AccNormalizer(Profile()); var first = map.Normalize(Raw(), now, 0); map.Reset();
    Check(first.SessionId != map.Normalize(Raw(), now, 0).SessionId);
});
Test("Session / graphics packet rollback starts new session", () =>
{
    var map = new AccNormalizer(Profile()); var first = map.Normalize(Raw(), now, 0);
    Check(first.SessionId != map.Normalize(Raw() with { GraphicsPacketId = 1 }, now.AddSeconds(1), 1000).SessionId);
});
Test("Query / milliseconds are converted to seconds", () =>
{
    var s = new AccNormalizer(Profile()).Normalize(Raw(), now, 0);
    Check(new TelemetryQueries().Answer("前车差多少", s, now).Text.Contains("1.84 秒"));
});
Test("Query / no substring match on negation", () => Check(!new TelemetryQueries().Answer("不要告诉我油量", SyntheticAdapter.Create(1, 0, now), now).HasVerifiedData));
Test("Query / synthetic origin is always labeled", () => Check(new TelemetryQueries().Answer("胎压", SyntheticAdapter.Create(1, 0, now), now).Text.StartsWith("【演示数据】")));
Test("Query / stale frame refused even when fields are valid", () => Check(!new TelemetryQueries().Answer("油量", SyntheticAdapter.Create(1, 0, now), now.AddSeconds(3)).HasVerifiedData));
Test("Query / future timestamp refused", () => Check(!new TelemetryQueries().Answer("油量", SyntheticAdapter.Create(1, 0, now), now.AddSeconds(-1)).HasVerifiedData));
Test("Query / any invalid wheel refuses full four-wheel answer", () =>
{
    var s = SyntheticAdapter.Create(1, 0, now); s.Fields["pressure.RR"] = s.Fields["pressure.RR"] with { Quality = DataQuality.Invalid };
    Check(!new TelemetryQueries().Answer("胎压", s, now).HasVerifiedData);
});

var temp = Path.Combine(Path.GetTempPath(), "DeltaTR.Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
AsyncTest("Recording / round trip preserves metadata and timestamps", async () =>
{
    var path = Path.Combine(temp, "roundtrip.jsonl");
    var original = SyntheticAdapter.Create(0, 0, now);
    await using (var recorder = new JsonlRecorder(path)) { Check(recorder.TryWrite(original)); }
    var read = new List<RaceSnapshot>();
    await foreach (var s in JsonlRecording.ReadAsync(path)) read.Add(s);
    Equal(1, read.Count); Equal(original.CapturedAt, read[0].CapturedAt); Equal(original.Fields["fuel"], read[0].Fields["fuel"]);
});
AsyncTest("Recording / refuses overwrite", async () =>
{
    var path = Path.Combine(temp, "exists.jsonl"); await File.WriteAllTextAsync(path, "KEEP");
    Throws<IOException>(() => new JsonlRecorder(path)); Equal("KEEP", await File.ReadAllTextAsync(path));
});
AsyncTest("Recording / corruption reports line number", async () =>
{
    var path = Path.Combine(temp, "broken.jsonl"); await File.WriteAllTextAsync(path, "{not json}\n");
    try { await foreach (var _ in JsonlRecording.ReadAsync(path)) { } }
    catch (InvalidDataException e) { Check(e.Message.Contains("1")); return; }
    throw new Exception("Corruption not rejected");
});
AsyncTest("Recording / reversed monotonic timestamps rejected", async () =>
{
    var path = Path.Combine(temp, "reversed.jsonl");
    await File.WriteAllLinesAsync(path, new[] { 200L, 100L }.Select(t => JsonSerializer.Serialize(SyntheticAdapter.Create(0, t, now), TelemetryJson.Options)));
    try { await foreach (var _ in JsonlRecording.ReadAsync(path)) { } }
    catch (InvalidDataException) { return; }
    throw new Exception("Reversed times not rejected");
});
AsyncTest("Recording / future schema rejected", async () =>
{
    var path = Path.Combine(temp, "version.jsonl");
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(SyntheticAdapter.Create(0, 0, now) with { SchemaVersion = 2 }, TelemetryJson.Options));
    try { await foreach (var _ in JsonlRecording.ReadAsync(path)) { } }
    catch (InvalidDataException) { return; }
    throw new Exception("Future schema not rejected");
});
AsyncTest("Replay / zero speed rejected", async () =>
{
    try { await foreach (var _ in JsonlRecording.ReplayAsync("unused", 0)) { } }
    catch (ArgumentOutOfRangeException) { return; }
    throw new Exception("Invalid speed not rejected");
});
AsyncTest("Replay / long gap remains cancellable", async () =>
{
    var path = Path.Combine(temp, "gap.jsonl");
    await File.WriteAllLinesAsync(path, new[] { 0L, 60000L }.Select(t => JsonSerializer.Serialize(SyntheticAdapter.Create(0, t, now.AddMilliseconds(t)), TelemetryJson.Options)));
    using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
    var count = 0;
    try { await foreach (var _ in JsonlRecording.ReplayAsync(path, 1, stop.Token)) count++; }
    catch (OperationCanceledException) { Equal(1, count); return; }
    throw new Exception("Playback cancellation did not stop long wait");
});
AsyncTest("Recording / bounded queue accounts every rejected frame", async () =>
{
    var path = Path.Combine(temp, "bounded.jsonl");
    var recorder = new JsonlRecorder(path, 1); var rejected = 0;
    var frame = SyntheticAdapter.Create(0, 0, now);
    for (var i = 0; i < 1000; i++) if (!recorder.TryWrite(frame)) rejected++;
    await recorder.DisposeAsync();
    Check(!recorder.TryWrite(frame)); rejected++;
    Equal((long)rejected, recorder.DroppedFrames);
    var count = 0; await foreach (var _ in JsonlRecording.ReadAsync(path)) count++;
    Equal(1001, count + rejected);
});
AsyncTest("CSV / metadata exported and origin retained", async () =>
{
    var input = Path.Combine(temp, "csv.jsonl"); var output = Path.Combine(temp, "csv.csv");
    await using (var recorder = new JsonlRecorder(input)) { recorder.TryWrite(SyntheticAdapter.Create(0, 0, now)); }
    await JsonlRecording.ExportCsvAsync(input, output);
    var content = await File.ReadAllTextAsync(output);
    Check(content.Contains("observedAt")); Check(content.Contains("Synthetic")); Check(content.Contains("pressure.FL"));
});

AsyncTest("Broadcast / local UDP v4 registration, seven incoming packet types, quality and reset", BroadcastTests.Loopback);
Test("Broadcast / enable preserves UTF16 configuration and passwords", () =>
{
    var file = Path.Combine(temp, "broadcasting.json");
    File.WriteAllText(file, """{"updListenerPort":0,"connectionPassword":"local","commandPassword":"private","other":123}""", new UnicodeEncoding(false, false));
    DeltaTR.Telemetry.ACC.Broadcast.BroadcastConfig.Enable(file);
    Equal((9000, "local"), DeltaTR.Telemetry.ACC.Broadcast.BroadcastConfig.Read(file));
    using var cfg = JsonDocument.Parse(File.ReadAllText(file, Encoding.Unicode));
    Check(File.ReadAllBytes(file)[0] == (byte)'{');
    Equal("private", cfg.RootElement.GetProperty("commandPassword").GetString()); Equal(123, cfg.RootElement.GetProperty("other").GetInt32());
});
Test("Full pages / tail fields, UTF16, active car slots and radio grammar", () =>
{
    var (p,g,s) = Bytes();
    BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(348), 475.25f);
    BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(712), 88.5f);
    BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(796), .125f);
    BinaryPrimitives.WriteInt32LittleEndian(g.AsSpan(252), 1);
    BinaryPrimitives.WriteSingleLittleEndian(g.AsSpan(256), 3.5f);
    BinaryPrimitives.WriteInt32LittleEndian(g.AsSpan(976), 12);
    BinaryPrimitives.WriteInt32LittleEndian(g.AsSpan(AccLayout.Graphics["globalYellow2"]), 1);
    BinaryPrimitives.WriteInt32LittleEndian(g.AsSpan(AccLayout.Graphics["penalty"]), 3);
    Encoding.Unicode.GetBytes("湿胎测试").CopyTo(s, 754);
    Encoding.Unicode.GetBytes("SensitiveTestDriver").CopyTo(s, 200);
    var raw = AccDecoder.Decode(p,g,s);
    var normalizer = new AccNormalizer(new());
    normalizer.Normalize(raw, now, 0);
    var time = now.AddMilliseconds(50);
    var frame = normalizer.Normalize(raw with { PhysicsPacketId=101, GraphicsPacketId=201 }, time, 50);
    var radio = new TelemetryQueries();
    Check(frame.RaceControl is { Yellow: true, Penalty: 3 });
    Check(radio.Answer("左前刹车温度多少", frame, time).Text.Contains("475.25"));
    Check(radio.Answer("水温多少", frame, time).Text.Contains("88.5"));
    Check(radio.Answer("湿胎名称", frame, time).Text.Contains("湿胎测试"));
    Check(radio.Answer("车辆槽位1的位置",frame,time).Text.Contains("编号 12，X 3.5"));
    Check(!radio.Answer("车辆槽位2的位置",frame,time).HasVerifiedData);
    Equal(.125, frame.Fields["Physics.absVibrations"].Value);
    Check(radio.Answer("本车车手名字", frame, time).Text.Contains("SensitiveTestDriver"));
    Check(!frame.Fields.ContainsKey("Static.playerName") && !JsonSerializer.Serialize(frame).Contains("SensitiveTestDriver"));
});
foreach (var game in Enum.GetValues<RacingGame>())
    Test($"Race alerts / {game} transitions, filtering, queue and session boundaries", () => RaceAlertTests.Run(game));
Test("LMU / player matching, wheel units, independent freshness, pause and replay", SimulatorTests.Lmu);
Test("iRacing / rotating SDK rows, units, session metadata and tyre report isolation", SimulatorTests.IRacing);
Test("iRacing / complete reference catalog, query coverage and unavailable fields", SimulatorTests.IRacingReferenceCatalog);
Test("iRacing / native pressure units, player indexing and session encoding", SimulatorTests.IRacingNativeUnits);
Test("Broadcast / rank loss survives missing sample and PTT, rival speed and units", AutomationTests.RankAndSpeed);
Test("Automation / race start, ordered actions, intervals, pause and game isolation", AutomationTests.EventsAndRules);
Test("Broadcast / speed and gap estimates, teleports and lapped traffic", AutomationTests.Reports);
Test("Automation / disabled, edited, silenced and invalid rules", AutomationTests.RuleChanges);
Test("Strategy / lap samples, fuel observations, pits and session boundaries", StrategyTests.Sampling);
AsyncTest("Strategy / preferences and fresh evidence reach AI with local identity excluded", StrategyTests.Request);
ValidationTests.Register(cases, now, temp);
VoiceTests.Register(cases, now);
TencentSpeechTests.Register(cases);
var failed = 0;
foreach (var test in cases)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception e) { failed++; Console.WriteLine("FAIL " + test.Name + " :: " + e.Message); }
}
Console.WriteLine($"\n{cases.Count - failed}/{cases.Count} passed; {failed} failed.");
// Only delete this runner's newly created temp directory, never caller data.
Directory.Delete(temp, true);
return failed == 0 ? 0 : 1;
