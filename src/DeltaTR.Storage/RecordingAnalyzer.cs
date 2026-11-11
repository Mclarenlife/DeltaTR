using System.Text.Json;
using DeltaTR.Core;
using DeltaTR.Core.Validation;

namespace DeltaTR.Storage;

public sealed record FieldQualitySummary(long PresentFrames, long MissingFrames, long FreshDecodedFrames,
    long VerifiedFreshFrames, double? FreshDecodedPercent, Dictionary<string, long> Qualities);
public sealed record RecordingQualityReport(int SchemaVersion, string InputFile, long TotalFrames, long SyntheticFrames,
    long RealLiveFrames, long SessionTransitions, double SpanSeconds, double CoveredRealLiveSeconds,
    long GapsOverTwoSeconds, long MaximumGapMilliseconds, double? RecordedFramesPerSecond,
    Dictionary<string, long> Phases, Dictionary<string, FieldQualitySummary> Fields, string GateStatus)
{
    public string Summary => $"共 {TotalFrames} 帧，跨度 {SpanSeconds:0.0} 秒；真实 Live {RealLiveFrames} 帧，合成 {SyntheticFrames} 帧。\n" +
        $"可覆盖的真实驾驶区间 {CoveredRealLiveSeconds:0.0} 秒；超过 2 秒的录制间隔 {GapsOverTwoSeconds} 次，最大 {MaximumGapMilliseconds} ms。\n" +
        "字段百分比以真实 Live 帧为分母，包含缺失字段；时间跨度和录制帧率不是游戏更新率。\n" + GateStatus;
}

public static class RecordingAnalyzer
{
    private sealed class FieldCounts
    {
        public long Present, Fresh, Verified;
        public Dictionary<string, long> Qualities { get; } = [];
    }

    public static async Task<RecordingQualityReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
    {
        long total = 0, synthetic = 0, live = 0, transitions = 0, covered = 0, gaps = 0, maxGap = 0;
        long first = 0, last = 0;
        RaceSnapshot? previous = null;
        var phases = new Dictionary<string, long>();
        var fields = new Dictionary<string, FieldCounts>();
        var lastSession = "";
        static bool RealLive(RaceSnapshot s) => s.Origin == DataOrigin.Live && s.Phase == SessionPhase.Live;
        await foreach (var s in JsonlRecording.ReadAsync(path, cancellationToken))
        {
            if (total++ == 0) first = s.ElapsedMilliseconds;
            last = s.ElapsedMilliseconds;
            if (s.Origin == DataOrigin.Synthetic) synthetic++;
            var phase = s.Origin + "/" + s.Phase;
            phases[phase] = phases.GetValueOrDefault(phase) + 1;
            if (s.SessionId.Length > 0 && s.SessionId != lastSession) { transitions++; lastSession = s.SessionId; }
            if (previous is not null)
            {
                var gap = s.ElapsedMilliseconds - previous.ElapsedMilliseconds;
                maxGap = Math.Max(maxGap, gap);
                if (gap > 2000) gaps++;
                else if (RealLive(previous) && RealLive(s) && previous.SessionId == s.SessionId) covered += gap;
            }
            previous = s;
            if (!RealLive(s)) continue;
            live++;
            // Entirely missing P0 fields must also appear in the report, rather than disappearing from the denominator.
            foreach (var metric in CalibrationCatalog.Metrics) fields.TryAdd(metric.Key, new());
            foreach (var (key, v) in s.Fields)
            {
                if (!fields.TryGetValue(key, out var count)) fields[key] = count = new();
                count.Present++;
                var quality = v.Quality.ToString();
                count.Qualities[quality] = count.Qualities.GetValueOrDefault(quality) + 1;
                if (v.Value is { } value && double.IsFinite(value) && v.Source.StartsWith("ACC.", StringComparison.Ordinal) &&
                    s.CapturedAt >= v.ObservedAt && s.CapturedAt - v.ObservedAt <= Metrics.MaxAge &&
                    v.Quality is DataQuality.Valid or DataQuality.NeedsVerification or DataQuality.Estimated)
                {
                    count.Fresh++;
                    if (v.Quality == DataQuality.Valid && !v.IsEstimated) count.Verified++;
                }
            }
        }
        if (total == 0) throw new InvalidDataException("录制文件没有快照，无法生成质量报告。");
        var duration = (last - first) / 1000d;
        return new(1, Path.GetFileName(path), total, synthetic, live, transitions, duration, covered / 1000d,
            gaps, maxGap, duration > 0 ? (total - 1) / duration : null, phases,
            fields.ToDictionary(k => k.Key, k => new FieldQualitySummary(k.Value.Present, live - k.Value.Present,
                k.Value.Fresh, k.Value.Verified, live > 0 ? 100d * k.Value.Fresh / live : null, k.Value.Qualities)),
            "G0 未自动判定：本报告不能证明 HUD/单位正确性、游戏性能影响或真实驾驶验收通过。");
    }

    public static async Task WriteReportAsync(RecordingQualityReport report, string path, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        await JsonSerializer.SerializeAsync(output, report, new JsonSerializerOptions(TelemetryJson.Options) { WriteIndented = true }, cancellationToken);
    }
}
