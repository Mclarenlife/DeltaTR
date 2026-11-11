using DeltaTR.Core;

namespace DeltaTR.Engineer;

public enum RadioTrigger
{
    Interval, RaceStart, PositionGained, PositionLost, Yellow, Green, Penalty, PenaltyCleared,
    Blue, PitEntry, PitExit, LapCompleted, RaceFinish
}

public enum RadioAction
{
    Position, GapLeader, GapAhead, GapBehind, AheadSpeed, BehindSpeed, Fuel, TyrePressure,
    TyreTemperature, LastLap, RemainingTime, Flags, Penalty
}

public sealed record AutomationRule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public RacingGame Game { get; init; }
    public string Name { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public RadioTrigger Trigger { get; init; } = RadioTrigger.Interval;
    public int IntervalSeconds { get; init; } = 120;
    public RadioAction[] Actions { get; init; } = [RadioAction.Position];

    public void Validate()
    {
        if (!Enum.IsDefined(Game) || !Enum.IsDefined(Trigger)) throw new ArgumentException("请选择有效的游戏和触发条件。");
        if (Name.Trim().Length is < 1 or > 60) throw new ArgumentException("名称需要 1 至 60 个字符。");
        if (IntervalSeconds is < 10 or > 3600) throw new ArgumentException("间隔需要在 10 至 3600 秒之间。");
        if (Actions == null || Actions.Length is < 1 or > 12 || Actions.Any(a => !Enum.IsDefined(a)))
            throw new ArgumentException("请选择 1 至 12 个播报动作。");
    }
}

public sealed record RaceNotice(RadioTrigger Trigger, string Text);

public static class AutomationLabels
{
    public static string Trigger(RadioTrigger trigger) => trigger switch
    {
        RadioTrigger.Interval => "固定时间间隔", RadioTrigger.RaceStart => "比赛开始时",
        RadioTrigger.PositionGained => "排名上升时", RadioTrigger.PositionLost => "排名下降时",
        RadioTrigger.Yellow => "出现黄旗时", RadioTrigger.Green => "黄旗解除时",
        RadioTrigger.Penalty => "收到处罚时", RadioTrigger.PenaltyCleared => "处罚清零时",
        RadioTrigger.Blue => "出现蓝旗时", RadioTrigger.PitEntry => "进入维修区时",
        RadioTrigger.PitExit => "离开维修区时", RadioTrigger.LapCompleted => "每完成一圈",
        _ => "比赛结束时"
    };
    public static string Action(RadioAction action) => action switch
    {
        RadioAction.Position => "播报当前排名", RadioAction.GapLeader => "播报与头车秒差",
        RadioAction.GapAhead or RadioAction.AheadSpeed => "播报与前车秒差", RadioAction.GapBehind => "播报与后车秒差",
        RadioAction.BehindSpeed => "播报与后车速度差",
        RadioAction.Fuel => "播报剩余燃油", RadioAction.TyrePressure => "播报胎压",
        RadioAction.TyreTemperature => "播报胎温", RadioAction.LastLap => "播报上一圈成绩",
        RadioAction.RemainingTime => "播报比赛剩余时间", RadioAction.Flags => "播报旗语",
        _ => "播报当前处罚"
    };
}
