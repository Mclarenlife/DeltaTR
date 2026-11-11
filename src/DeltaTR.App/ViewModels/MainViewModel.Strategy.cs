using System.ComponentModel;
using System.Windows.Input;
using DeltaTR.Core;
using DeltaTR.Engineer;

namespace DeltaTR.App.ViewModels;

public sealed record LengthChoice(ReplyLength Id, string Name);
public sealed record ApproachChoice(StrategyApproach Id, string Name);

public sealed class StrategyAnalysis(string title, string description) : INotifyPropertyChanged
{
    public string Title { get; } = title;
    public string Description { get; } = description;
    public ICommand RunCommand { get; internal set; } = null!;
    public string Evidence { get; private set; } = "等待驾驶数据";
    public string Result { get; private set; } = "点击分析，获取工程师建议。";
    public string UpdatedText { get; private set; } = "";
    internal int RequestVersion { get; set; } = -1;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void SetEvidence(string value)
    {
        if (Evidence == value) return;
        Evidence = value; PropertyChanged?.Invoke(this, new(nameof(Evidence)));
    }
    internal void SetResult(string text, string stamp = "")
    {
        Result = text; UpdatedText = stamp;
        PropertyChanged?.Invoke(this, new(nameof(Result)));
        PropertyChanged?.Invoke(this, new(nameof(UpdatedText)));
    }
    internal void Reset()
    {
        RequestVersion = -1;
        SetResult("点击分析，获取工程师建议。");
    }
}

public sealed partial class MainViewModel
{
    private readonly StrategyTelemetry strategyTelemetry = new();
    private string? strategySession;
    private string strategyInstructions = "", strategyStatus = "";
    private bool showPit = true, showTyres = true, showPace = true;
    private LengthChoice? strategyLength;
    private ApproachChoice? strategyApproach;
    public IReadOnlyList<LengthChoice> StrategyLengths { get; } =
        [new(ReplyLength.Brief, "简短 · 驾驶中收听"), new(ReplyLength.Normal, "标准 · 建议与依据"), new(ReplyLength.Detailed, "详细 · 比较方案")];
    public IReadOnlyList<ApproachChoice> StrategyApproaches { get; } =
        [new(StrategyApproach.Conservative, "稳健 · 完赛和余量优先"), new(StrategyApproach.Balanced, "均衡 · 速度与可靠性"), new(StrategyApproach.Aggressive, "进取 · 时间和位置优先")];
    public LengthChoice StrategyLength { get => strategyLength ?? StrategyLengths[0]; set { strategyLength = value; Changed(); } }
    public ApproachChoice StrategyBias { get => strategyApproach ?? StrategyApproaches[1]; set { strategyApproach = value; Changed(); } }
    public string StrategyInstructions { get => strategyInstructions; set { strategyInstructions = value; Changed(); } }
    public bool ShowPitAnalysis { get => showPit; set { showPit = value; Changed(); Changed(nameof(NoStrategyCards)); } }
    public bool ShowTyreAnalysis { get => showTyres; set { showTyres = value; Changed(); Changed(nameof(NoStrategyCards)); } }
    public bool ShowPaceAnalysis { get => showPace; set { showPace = value; Changed(); Changed(nameof(NoStrategyCards)); } }
    public bool NoStrategyCards => !ShowPitAnalysis && !ShowTyreAnalysis && !ShowPaceAnalysis;
    public string StrategyStatus { get => strategyStatus; private set { strategyStatus = value; Changed(); } }
    public string StrategyDataStatus => $"{SourceLabel} · 本次会话已采样 {strategyTelemetry.Laps.Count} 个完整圈";
    public StrategyAnalysis PitAnalysis { get; } = new("进站分析", "结合剩余燃油、比赛进度和进站要求，判断进站时机。");
    public StrategyAnalysis TyreAnalysis { get; } = new("换胎分析", "结合温度、压力、天气和可用的磨耗读数，分析换胎取舍。");
    public StrategyAnalysis PaceAnalysis { get; } = new("圈速保持分析", "使用近期完整圈的平均成绩、波动和变化趋势，讨论配速。");
    public ICommand SaveStrategyCommand { get; private set; } = null!;
    public ICommand ResetStrategyCommand { get; private set; } = null!;
    private StrategyProfile CurrentStrategy => activeSettings.Strategies.GetValueOrDefault(SelectedGame.Id?.ToString() ?? "") ?? new();

    private void InitializeStrategy()
    {
        SaveStrategyCommand = GameCommand(() =>
        {
            var profile = new StrategyProfile { ReplyLength = StrategyLength.Id, Approach = StrategyBias.Id,
                Instructions = StrategyInstructions.Trim(), ShowPit = ShowPitAnalysis, ShowTyres = ShowTyreAnalysis, ShowPace = ShowPaceAnalysis };
            profile.Validate();
            var profiles = new Dictionary<string, StrategyProfile>(activeSettings.Strategies) { [SelectedGame.Id!.Value.ToString()] = profile };
            var next = activeSettings with { Strategies = profiles };
            next.Save(settingsPath); activeSettings = next;
            CancelVoice(); ResetStrategyResults();
            StrategyStatus = $"已保存到 {GameName}，新的 AI 对话使用这些偏好。";
            return Task.CompletedTask;
        }, error => StrategyStatus = "保存失败：" + error.Message);
        ResetStrategyCommand = GameCommand(() =>
        {
            LoadStrategyProfile(new()); StrategyStatus = "已恢复默认选项，点击保存后生效。"; return Task.CompletedTask;
        }, error => StrategyStatus = error.Message);
        PitAnalysis.RunCommand = StrategyCommand(StrategyTopic.Pit, PitAnalysis);
        TyreAnalysis.RunCommand = StrategyCommand(StrategyTopic.Tyres, TyreAnalysis);
        PaceAnalysis.RunCommand = StrategyCommand(StrategyTopic.Pace, PaceAnalysis);
        LoadStrategy();
    }

    private ICommand StrategyCommand(StrategyTopic topic, StrategyAnalysis card) => GameCommand(async () =>
    {
        if (IsListening || processing) { StrategyStatus = "请先完成当前语音输入。"; return; }
        var source = stopped ? null : store.Latest;
        if (source is not { Phase: SessionPhase.Live } || !RaceReports.Fresh(source.CapturedAt, DateTimeOffset.UtcNow))
        {
            card.SetResult("请先连接游戏进入驾驶，或启动演示数据。"); return;
        }
        var sessionId = source.SessionId;
        int request = generation + 1;
        card.RequestVersion = request;
        card.SetResult("工程师正在分析…");
        await SubmitQuestionAsync(activeSettings.WakeWord + "，" + StrategyProfile.Question(topic));
        if (card.RequestVersion != request) return;
        if (generation != request || store.Latest?.SessionId != sessionId)
        {
            card.SetResult("本次分析已取消，请重新分析。"); return;
        }
        card.SetResult(Answer, $"依据 {source.CapturedAt.ToLocalTime():HH:mm:ss} 的遥测 · {GameName}");
    }, error => { card.SetResult("分析失败：" + error.Message); StrategyStatus = error.Message; });

    private void LoadStrategy()
    {
        LoadStrategyProfile(CurrentStrategy);
        ResetStrategyResults();
        StrategyStatus = "保存后应用于当前游戏的 AI 对话。";
    }
    private void LoadStrategyProfile(StrategyProfile profile)
    {
        StrategyLength = StrategyLengths.FirstOrDefault(p => p.Id == profile.ReplyLength) ?? StrategyLengths[0];
        StrategyBias = StrategyApproaches.FirstOrDefault(p => p.Id == profile.Approach) ?? StrategyApproaches[1];
        StrategyInstructions = profile.Instructions ?? "";
        ShowPitAnalysis = profile.ShowPit; ShowTyreAnalysis = profile.ShowTyres; ShowPaceAnalysis = profile.ShowPace;
    }
    private void ResetStrategyResults() { PitAnalysis.Reset(); TyreAnalysis.Reset(); PaceAnalysis.Reset(); }

    private void UpdateStrategy(RaceSnapshot? frame, DateTimeOffset now)
    {
        strategyTelemetry.Observe(frame, now);
        if (strategySession != frame?.SessionId)
        {
            strategySession = frame?.SessionId; ResetStrategyResults();
        }
        Changed(nameof(StrategyDataStatus));
        bool live = frame?.Phase == SessionPhase.Live && RaceReports.Fresh(frame.CapturedAt, now);
        string Value(string key)
        {
            var reading = shown?.Reading(key);
            if (!live || reading?.IsUsable(now, Metrics.MaxAge) != true || !reading.Value.HasValue) return "暂无";
            return reading.Unit switch
            {
                "ms" => $"{reading.Value / 60000:0.0} 分钟",
                "s" => $"{reading.Value / 60:0.0} 分钟",
                "lap" => $"{reading.Value:0} 圈",
                _ => $"{reading.Value:0.0} {reading.Unit}"
            };
        }
        PitAnalysis.SetEvidence($"剩余燃油 {Value("fuel")} · 已完成 {Value("completedLaps")} · 剩余时间 {Value("sessionTimeLeft")}");
        var measured = frame?.Game == RacingGame.IRacing;
        TyreAnalysis.SetEvidence(measured
            ? $"左前上次测量胎温 {Value("pitTemp.FL")} · 冷胎压设定 {Value("coldPressure.FL")}"
            : $"左前当前胎温 {Value("temperature.FL")} · 胎压 {Value("pressure.FL")}");
        var laps = strategyTelemetry.Laps;
        PaceAnalysis.SetEvidence(laps.Count > 0
            ? $"最近 {laps.Count} 圈平均 {laps.Average(l => l.Seconds):0.000} 秒 · 最快与最慢相差 {laps.Max(l => l.Seconds) - laps.Min(l => l.Seconds):0.000} 秒"
            : "正在等待完整圈采样，连续驾驶后可分析圈速变化。");
    }
}
