namespace DeltaTR.Core.Validation;

public sealed record CalibrationMetric(string Key, string[] Units, double Tolerance);

public static class CalibrationCatalog
{
    public static IReadOnlyList<CalibrationMetric> Metrics { get; } = Build();
    private static CalibrationMetric[] Build() => [
        new("speed", ["km/h"], 1), new("fuel", ["L", "kg"], 0.1),
        new("airTemperature", ["°C"], 1), new("roadTemperature", ["°C"], 1),
        new("position", ["position"], 0), new("completedLaps", ["lap"], 0),
        new("lastLapTime", ["ms"], 20), new("gapAhead", ["ms"], 20),
        new("gapBehind", ["ms"], 20), new("sessionTimeLeft", ["ms", "s"], 1),
        new("rainIntensity", ["category"], 0), new("missingMandatoryPits", ["count"], 0),
        .. Core.Metrics.Wheels.Select(w => new CalibrationMetric("pressure." + w, ["psi"], 0.15)),
        .. Core.Metrics.Wheels.Select(w => new CalibrationMetric("temperature." + w, ["°C"], 1))
    ];
}

/// <summary>A frozen, fresh live sample. HUD entry may happen later without silently changing the raw value.</summary>
public sealed class CalibrationCapture
{
    public RaceSnapshot Snapshot { get; }
    public DateTimeOffset FrozenAt { get; }
    private CalibrationCapture(RaceSnapshot snapshot, DateTimeOffset now)
    {
        Snapshot = snapshot with { Fields = new(snapshot.Fields) };
        FrozenAt = now;
    }
    public static CalibrationCapture Freeze(RaceSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Origin != DataOrigin.Live || snapshot.Phase != SessionPhase.Live ||
            now < snapshot.CapturedAt || now - snapshot.CapturedAt > Core.Metrics.MaxAge ||
            new[] { snapshot.GameVersion, snapshot.SharedMemoryVersion, snapshot.CarModel, snapshot.Track, snapshot.SessionId }
                .Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("只能冻结新鲜的真实 ACC 驾驶数据；演示、历史回放和非驾驶状态不能校准。");
        return new(snapshot, now);
    }
}

public sealed record HudComparison(string Metric, double RawValue, double HudValue, string Unit, double Tolerance,
    string Source, DateTimeOffset ObservedAt, DateTimeOffset FrozenAt, string SessionId, string Note);
public sealed record CalibrationEvidence(int SchemaVersion, string GameVersion, string SharedMemoryVersion,
    string CarModel, string Track, string OperatorNote, DateTimeOffset CreatedAt, HudComparison[] Comparisons);

public sealed class CalibrationDraft
{
    private readonly List<HudComparison> comparisons = [];
    private readonly RaceSnapshot context;
    public CalibrationDraft(CalibrationCapture first) => context = first.Snapshot;
    public IReadOnlyList<HudComparison> Comparisons => comparisons.AsReadOnly();
    public string ContextLabel => $"{context.CarModel} @ {context.Track} · {context.GameVersion} / {context.SharedMemoryVersion}";
    public string[] ReadyFields => comparisons.GroupBy(c => c.Metric).Where(g => g.Count() >= 2).Select(g => g.Key).ToArray();

    public void Add(CalibrationCapture capture, string key, double hudValue, string unit, string note)
    {
        var s = capture.Snapshot;
        if (s.GameVersion != context.GameVersion || s.SharedMemoryVersion != context.SharedMemoryVersion ||
            s.CarModel != context.CarModel || s.Track != context.Track)
            throw new InvalidOperationException("版本、车型或赛道已变化，请重新创建校准草稿。");
        var spec = CalibrationCatalog.Metrics.SingleOrDefault(m => m.Key == key)
            ?? throw new ArgumentException("该字段尚未纳入校准向导。");
        if (!spec.Units.Contains(unit) || !double.IsFinite(hudValue) || string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("请选择支持的单位，填写有限的 HUD 数值与场景备注。");
        if (!s.Fields.TryGetValue(key, out var v) || v.Value is not { } raw || !double.IsFinite(raw) || v.IsEstimated ||
            v.Quality is not (DataQuality.Valid or DataQuality.NeedsVerification) ||
            capture.FrozenAt < v.ObservedAt || capture.FrozenAt - v.ObservedAt > Core.Metrics.MaxAge ||
            !v.Source.StartsWith("ACC.", StringComparison.Ordinal))
            throw new InvalidOperationException("冻结帧的这个字段无效、过期、为估计值或来源不符，无法核验。");
        if (Math.Abs(raw - hudValue) > spec.Tolerance + 1e-6)
            throw new InvalidOperationException($"HUD 与原始值偏差超出 {spec.Tolerance} {unit}，请核对单位和采样时刻，不会加入校准。");
        if (comparisons.Any(c => c.Metric == key && c.SessionId == s.SessionId && c.ObservedAt == v.ObservedAt))
            throw new InvalidOperationException("此来源帧已核验，请冻结一个更新的样本。");
        if (comparisons.Any(c => c.Metric == key && c.Unit != unit))
            throw new InvalidOperationException("同一字段的单位不能混用；请重新开始草稿核对单位。");
        comparisons.Add(new(key, raw, hudValue, unit, spec.Tolerance, v.Source, v.ObservedAt, capture.FrozenAt, s.SessionId, note.Trim()));
    }

    public (CalibrationProfile Profile, CalibrationEvidence Evidence) Export(string note, DateTimeOffset now)
    {
        var ready = ReadyFields;
        if (ready.Length == 0 || string.IsNullOrWhiteSpace(note))
            throw new InvalidOperationException("至少一个字段需要两个不同来源帧的合格对照，并填写本次实验说明。");
        var profile = new CalibrationProfile
        {
            GameVersion = context.GameVersion, SharedMemoryVersion = context.SharedMemoryVersion,
            CarModel = context.CarModel, Track = context.Track,
            VerifiedUnits = ready.ToDictionary(k => k, k => comparisons.First(c => c.Metric == k).Unit)
        };
        return (profile, new(1, context.GameVersion, context.SharedMemoryVersion, context.CarModel, context.Track,
            note.Trim(), now, comparisons.ToArray()));
    }
}
