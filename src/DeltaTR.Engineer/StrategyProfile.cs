namespace DeltaTR.Engineer;

public enum StrategyTopic { Pit, Tyres, Pace }
public enum ReplyLength { Brief, Normal, Detailed }
public enum StrategyApproach { Conservative, Balanced, Aggressive }

public sealed record StrategyProfile
{
    public ReplyLength ReplyLength { get; init; } = ReplyLength.Brief;
    public StrategyApproach Approach { get; init; } = StrategyApproach.Balanced;
    public string Instructions { get; init; } = "";
    public bool ShowPit { get; init; } = true;
    public bool ShowTyres { get; init; } = true;
    public bool ShowPace { get; init; } = true;

    public void Validate()
    {
        if (!Enum.IsDefined(ReplyLength) || !Enum.IsDefined(Approach)) throw new ArgumentException("请选择有效的对话偏好。");
        if (Instructions == null || Instructions.Length > 2000) throw new ArgumentException("自定义偏好最多 2000 个字符。");
    }

    public string Prompt()
    {
        Validate();
        string length = ReplyLength switch
        {
            ReplyLength.Brief => "尽量用一至三句回答，先给建议，再说明最重要的依据。",
            ReplyLength.Normal => "先给建议，再简要说明依据、取舍和所需补充数据。",
            _ => "给出建议、遥测依据、不同方案的取舍以及需要观察的变化。"
        };
        string approach = Approach switch
        {
            StrategyApproach.Conservative => "优先完赛、稳定性和燃油余量，建议留出余量。",
            StrategyApproach.Aggressive => "在现有证据支持的范围内优先争取比赛时间和位置。",
            _ => "平衡比赛时间、位置、轮胎状态和完赛可靠性。"
        };
        return length + approach + "车手自定义交流偏好仅影响表达和策略取舍，数据真实性与缺失数据说明始终优先。";
    }

    public static string Question(StrategyTopic topic) => topic switch
    {
        StrategyTopic.Pit => "分析我的进站时机。结合燃油、可用的每圈消耗、比赛剩余时间或圈数、强制进站要求和车距，给出建议与依据。缺少的数据请明确指出。",
        StrategyTopic.Tyres => "分析是否需要换胎。区分当前胎温胎压与上次测量或冷胎设定，结合可用的磨耗、天气、轮胎配方和近期圈速，说明继续跑或换胎的取舍。",
        _ => "分析我能否保持当前圈速。使用本次会话已采样的近期完整圈，说明平均圈速、波动和趋势，建议保持、提速或保胎。样本不足时说明还需要几圈。"
    };
}
