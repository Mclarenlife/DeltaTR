using System.Text.Json;
using DeltaTR.Core;
using DeltaTR.Core.Validation;
using DeltaTR.Storage;
using DeltaTR.Telemetry.ACC;

internal static class ValidationTests
{
    public static void Register(List<(string Name, Func<Task> Run)> cases, DateTimeOffset now, string temp)
    {
        void Test(string name, Action action) => cases.Add((name, () => { action(); return Task.CompletedTask; }));
        void Async(string name, Func<Task> action) => cases.Add((name, action));
        void Check(bool condition) { if (!condition) throw new Exception("Assertion failed"); }
        void Reject(Action action)
        {
            try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException) { return; }
            throw new Exception("Expected validation rejection");
        }
        RaceSnapshot Live(long milliseconds = 0)
        {
            // Manufactured test inputs ONLY. Never used by an application adapter or saved as a real-world fixture.
            var s = SyntheticAdapter.Create(1, milliseconds, now.AddMilliseconds(milliseconds), "TEST-ONLY");
            return s with { Origin = DataOrigin.Live, Fields = s.Fields.ToDictionary(x => x.Key,
                x => x.Value with { Source = "ACC.Test." + x.Key, Quality = DataQuality.NeedsVerification }) };
        }
        CalibrationCapture Capture(long ms = 0) => CalibrationCapture.Freeze(Live(ms), now.AddMilliseconds(ms));
        CalibrationDraft Draft()
        {
            var draft = new CalibrationDraft(Capture());
            draft.Add(Capture(), "airTemperature", 24, "°C", "TEST ONLY sample one");
            draft.Add(Capture(1000), "airTemperature", 24, "°C", "TEST ONLY sample two");
            return draft;
        }
        async Task<string> FileOf(params RaceSnapshot[] snapshots)
        {
            var file = Path.Combine(temp, Guid.NewGuid() + ".jsonl");
            await File.WriteAllLinesAsync(file, snapshots.Select(s => JsonSerializer.Serialize(s, TelemetryJson.Options)));
            return file;
        }

        Test("Calibration / synthetic capture is rejected", () => Reject(() => CalibrationCapture.Freeze(SyntheticAdapter.Create(0, 0, now), now)));
        Test("Calibration / paused capture is rejected", () => Reject(() => CalibrationCapture.Freeze(Live() with { Phase = SessionPhase.Paused }, now)));
        Test("Calibration / stale capture is rejected", () => Reject(() => CalibrationCapture.Freeze(Live(), now.AddSeconds(3))));
        Test("Calibration / future capture is rejected", () => Reject(() => CalibrationCapture.Freeze(Live(), now.AddSeconds(-1))));
        Test("Calibration / metadata must be complete", () => Reject(() => CalibrationCapture.Freeze(Live() with { Track = "" }, now)));
        Test("Calibration / frozen sample does not follow source mutation", () =>
        {
            var input = Live(); var capture = CalibrationCapture.Freeze(input, now);
            input.Fields["airTemperature"] = input.Fields["airTemperature"] with { Value = 50 };
            Check(capture.Snapshot.Fields["airTemperature"].Value == 24);
        });
        Test("Calibration / one sample cannot authorize a field", () =>
        {
            var draft = new CalibrationDraft(Capture()); draft.Add(Capture(), "fuel", 45, "L", "TEST");
            Check(draft.ReadyFields.Length == 0); Reject(() => draft.Export("TEST", now));
        });
        Test("Calibration / two frames export only qualifying fields", () =>
        {
            var draft = Draft(); draft.Add(Capture(), "fuel", 45, "L", "TEST");
            var result = draft.Export("TEST", now);
            Check(result.Profile.VerifiedUnits.Count == 1 && result.Profile.VerifiedUnits.ContainsKey("airTemperature"));
            Check(result.Evidence.Comparisons.Length == 3);
        });
        Test("Calibration / repeated source frame is rejected", () =>
        {
            var draft = Draft(); Reject(() => draft.Add(Capture(), "airTemperature", 24, "°C", "TEST"));
        });
        Test("Calibration / differing car context is rejected", () =>
        {
            var capture = CalibrationCapture.Freeze(Live(1000) with { CarModel = "another" }, now.AddSeconds(1));
            Reject(() => Draft().Add(capture, "fuel", 45, "L", "TEST"));
        });
        Test("Calibration / HUD mismatch does not add evidence", () =>
        {
            var draft = Draft(); Reject(() => draft.Add(Capture(), "fuel", 10, "L", "TEST")); Check(draft.Comparisons.Count == 2);
        });
        Test("Calibration / non-finite HUD input is rejected", () => Reject(() => Draft().Add(Capture(), "fuel", double.NaN, "L", "TEST")));
        Test("Calibration / fuel unit cannot change mid-draft", () =>
        {
            var draft = Draft(); draft.Add(Capture(), "fuel", 45, "L", "TEST");
            Reject(() => draft.Add(Capture(1000), "fuel", 45, "kg", "TEST"));
        });
        Test("Calibration / wrong unit and missing note are rejected", () =>
        {
            Reject(() => Draft().Add(Capture(), "fuel", 45, "gal", "TEST"));
            Reject(() => Draft().Add(Capture(), "fuel", 45, "L", ""));
        });
        foreach (var quality in new[] { DataQuality.Stale, DataQuality.Invalid, DataQuality.Estimated })
        Test($"Calibration / {quality} field cannot be approved", () =>
        {
            var s = Live(); s.Fields["fuel"] = s.Fields["fuel"] with { Quality = quality };
            Reject(() => Draft().Add(CalibrationCapture.Freeze(s, now), "fuel", 45, "L", "TEST"));
        });
        Test("Calibration / old field within a fresh snapshot is rejected", () =>
        {
            var s = Live(); s.Fields["fuel"] = s.Fields["fuel"] with { ObservedAt = now.AddSeconds(-3) };
            Reject(() => Draft().Add(CalibrationCapture.Freeze(s, now), "fuel", 45, "L", "TEST"));
        });
        Async("Calibration / saved bundle reloads and detects modified evidence", async () =>
        {
            var path = Path.Combine(temp, "calibration.json");
            await CalibrationBundle.SaveAsync(Draft(), path, "AUTOMATED TEST ONLY");
            var profile = CalibrationProfile.Load(path);
            Check(profile.EvidenceSha256?.Length == 64 && profile.VerifiedUnits.Count == 1);
            await File.AppendAllTextAsync(Path.Combine(temp, profile.Evidence), " ");
            Reject(() => CalibrationProfile.Load(path));
        });
        Async("Calibration / missing evidence is rejected", async () =>
        {
            var path = Path.Combine(temp, "missing-evidence.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new CalibrationProfile { Evidence = "missing.json", EvidenceSha256 = "abc" }, TelemetryJson.Options));
            Reject(() => CalibrationProfile.Load(path));
        });
        Async("Calibration / never overwrites profile", async () =>
        {
            var path = Path.Combine(temp, "keep.json"); await File.WriteAllTextAsync(path, "KEEP");
            try { await CalibrationBundle.SaveAsync(Draft(), path, "TEST"); }
            catch (IOException) { Check(await File.ReadAllTextAsync(path) == "KEEP"); return; }
            throw new Exception("Overwrite allowed");
        });
        Async("Analysis / synthetic samples never count as real driving", async () =>
        {
            var path = await FileOf(SyntheticAdapter.Create(0, 0, now), SyntheticAdapter.Create(1, 1000, now.AddSeconds(1)));
            var report = await RecordingAnalyzer.AnalyzeAsync(path);
            Check(report.SyntheticFrames == 2 && report.RealLiveFrames == 0 && report.Fields.Count == 0 && report.CoveredRealLiveSeconds == 0);
        });
        Async("Analysis / missing fields reduce availability denominator", async () =>
        {
            var second = Live(1000); second.Fields.Remove("fuel");
            var report = await RecordingAnalyzer.AnalyzeAsync(await FileOf(Live(), second));
            var fuel = report.Fields["fuel"];
            Check(fuel.MissingFrames == 1 && fuel.FreshDecodedPercent == 50 && fuel.VerifiedFreshFrames == 0);
            Check(report.CoveredRealLiveSeconds == 1 && report.RecordedFramesPerSecond == 1);
        });
        Async("Analysis / large holes are not claimed as covered driving", async () =>
        {
            var report = await RecordingAnalyzer.AnalyzeAsync(await FileOf(Live(), Live(60000)));
            Check(report.GapsOverTwoSeconds == 1 && report.MaximumGapMilliseconds == 60000 && report.CoveredRealLiveSeconds == 0);
        });
        Async("Analysis / never-observed P0 fields are still reported missing", async () =>
        {
            var s = Live(); s.Fields.Remove("fuel");
            var report = await RecordingAnalyzer.AnalyzeAsync(await FileOf(s));
            Check(report.Fields["fuel"].PresentFrames == 0 && report.Fields["fuel"].MissingFrames == 1 && report.Fields["fuel"].FreshDecodedPercent == 0);
        });
        Async("Analysis / pause and session change do not extend covered driving", async () =>
        {
            var report = await RecordingAnalyzer.AnalyzeAsync(await FileOf(Live(), Live(1000) with { Phase = SessionPhase.Paused }, Live(2000) with { SessionId = "NEXT" }));
            Check(report.CoveredRealLiveSeconds == 0 && report.RealLiveFrames == 2 && report.SessionTransitions == 2);
        });
        Async("Analysis / single-frame rate is undefined", async () =>
        {
            var report = await RecordingAnalyzer.AnalyzeAsync(await FileOf(Live()));
            Check(report.RecordedFramesPerSecond is null && report.SpanSeconds == 0);
        });
        Async("Analysis / empty input is rejected", async () =>
        {
            try { await RecordingAnalyzer.AnalyzeAsync(await FileOf()); }
            catch (InvalidDataException) { return; }
            throw new Exception("Empty report accepted");
        });
        Async("Analysis / stale values do not count as fresh", async () =>
        {
            var s = Live(); s.Fields["fuel"] = s.Fields["fuel"] with { Quality = DataQuality.Valid, ObservedAt = now.AddSeconds(-4) };
            var report = await RecordingAnalyzer.AnalyzeAsync(await FileOf(s));
            Check(report.Fields["fuel"].FreshDecodedFrames == 0 && report.Fields["fuel"].VerifiedFreshFrames == 0);
        });
        Async("Analysis / report persists without claiming G0", async () =>
        {
            var report = await RecordingAnalyzer.AnalyzeAsync(await FileOf(Live()));
            var path = Path.Combine(temp, "quality.json"); await RecordingAnalyzer.WriteReportAsync(report, path);
            var read = JsonSerializer.Deserialize<RecordingQualityReport>(await File.ReadAllTextAsync(path), TelemetryJson.Options)!;
            Check(read.TotalFrames == 1 && read.GateStatus.Contains("未自动判定"));
            try { await RecordingAnalyzer.WriteReportAsync(report, path); }
            catch (IOException) { return; }
            throw new Exception("Report overwrite allowed");
        });
    }
}
