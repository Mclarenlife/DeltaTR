using DeltaTR.Core;

namespace DeltaTR.Engineer;

public sealed class RadioAutomation
{
    private sealed record Schedule(string Signature, double Next);
    private sealed record Waiting(AutomationRule Rule, DateTimeOffset At);
    private readonly Dictionary<Guid, Schedule> schedules = [];
    private readonly Dictionary<Guid, Waiting> pending = [];
    private (RacingGame, DataOrigin, string)? session;
    private DateTimeOffset? lastCapture;
    private double drivingSeconds;
    public int PendingCount => pending.Count;

    public void ClearPending() => pending.Clear();

    public void Observe(RaceSnapshot? frame, DateTimeOffset now, IReadOnlyList<AutomationRule> rules, IReadOnlyList<RaceNotice> events)
    {
        if (frame == null || frame.Phase != SessionPhase.Live || !RaceReports.Fresh(frame.CapturedAt, now))
        {
            lastCapture = null; pending.Clear();
            return;
        }
        var identity = (frame.Game, frame.Origin, frame.SessionId);
        if (session != identity)
        {
            session = identity; schedules.Clear(); pending.Clear(); drivingSeconds = 0; lastCapture = null;
        }
        if (lastCapture is { } previous && frame.CapturedAt > previous && frame.CapturedAt - previous <= Metrics.MaxAge)
            drivingSeconds += (frame.CapturedAt - previous).TotalSeconds;
        lastCapture = frame.CapturedAt;
        var active = rules.Where(r => r.Game == frame.Game && r.Enabled && Valid(r)).GroupBy(r => r.Id).Select(g => g.First()).Take(40).ToArray();
        foreach (var id in schedules.Keys.Except(active.Select(r => r.Id)).ToArray()) { schedules.Remove(id); pending.Remove(id); }
        foreach (var rule in active)
        {
            var signature = $"{rule.Name}|{rule.Trigger}|{rule.IntervalSeconds}|{string.Join(',', rule.Actions)}";
            if (!schedules.TryGetValue(rule.Id, out var schedule) || schedule.Signature != signature)
            {
                schedule = new(signature, drivingSeconds + rule.IntervalSeconds);
                schedules[rule.Id] = schedule; pending.Remove(rule.Id);
            }
            if (pending.TryGetValue(rule.Id, out var queued) && now - queued.At > TimeSpan.FromSeconds(30)) pending.Remove(rule.Id);
            if (rule.Trigger == RadioTrigger.Interval && drivingSeconds >= schedule.Next)
            {
                schedules[rule.Id] = schedule with { Next = drivingSeconds + rule.IntervalSeconds };
                pending[rule.Id] = new(rule, now);
            }
            else if (rule.Trigger != RadioTrigger.Interval && events.Any(e => e.Trigger == rule.Trigger))
                pending[rule.Id] = new(rule, now);
            if (rule.Trigger == RadioTrigger.Yellow && frame.RaceControl?.Yellow != true ||
                rule.Trigger == RadioTrigger.Blue && frame.RaceControl?.Blue != true ||
                rule.Trigger == RadioTrigger.Green && frame.RaceControl?.Yellow != false ||
                rule.Trigger == RadioTrigger.Penalty && frame.RaceControl?.Penalty is null or <= 0)
                pending.Remove(rule.Id);
        }
    }

    private static bool Valid(AutomationRule rule)
    {
        try { rule.Validate(); return true; }
        catch (ArgumentException) { return false; }
    }

    public string? TakeReady(RaceSnapshot frame, DateTimeOffset now, RaceReports reports, bool imperial)
    {
        var next = pending.OrderBy(p => p.Value.At).FirstOrDefault();
        if (next.Value == null) return null;
        pending.Remove(next.Key);
        return Render(next.Value.Rule, frame, now, reports, imperial);
    }

    public static string Render(AutomationRule rule, RaceSnapshot frame, DateTimeOffset now, RaceReports reports, bool imperial)
    {
        if (frame.Phase != SessionPhase.Live || frame.Game != rule.Game || !RaceReports.Fresh(frame.CapturedAt, now))
            return "等待当前游戏的有效驾驶数据。";
        var query = new TelemetryQueries(new Dictionary<string, string[]>(), "", frame.Game, frame.Native?.Catalog);
        var display = DisplayUnits.ForDisplay(frame, imperial)!;
        string Query(string metric)
        {
            var reply = query.AnswerMetric(metric, display, now);
            return reply.Text.Replace("【演示数据】", "");
        }
        string Read(RadioAction action)
        {
            var rank = RaceReports.Position(frame, now)?.Value;
            switch (action)
            {
                case RadioAction.Position: return rank.HasValue ? $"目前第 {rank:0} 名。" : "当前排名暂不可用。";
                case RadioAction.GapLeader: return reports.Gap(frame, now, true);
                case RadioAction.GapAhead: return reports.Gap(frame, now, false);
                case RadioAction.GapBehind: return reports.GapBehind(frame, now);
                case RadioAction.AheadSpeed:
                case RadioAction.BehindSpeed:
                    if (!rank.HasValue) return "当前排名暂不可用。";
                    var target = frame.Traffic?.AtPosition((int)rank + (action == RadioAction.AheadSpeed ? -1 : 1));
                    return (action == RadioAction.AheadSpeed ? "前车，" : "后车，") + RaceReports.SpeedDifference(frame, target, now, imperial);
                case RadioAction.Fuel: return Query("fuel");
                case RadioAction.TyrePressure: return frame.Game == RacingGame.IRacing ? string.Concat(Metrics.Wheels.Select(w => Query("coldPressure." + w))) : Query("pressure");
                case RadioAction.TyreTemperature: return frame.Game == RacingGame.IRacing ? string.Concat(Metrics.Wheels.Select(w => Query("pitTemp." + w))) : Query("temperature");
                case RadioAction.LastLap: return Query("lastLapTime");
                case RadioAction.RemainingTime: return Query("sessionTimeLeft");
                case RadioAction.Flags: return Query(frame.Game == RacingGame.ACC ? "Graphics.flag" : "RaceFeed.flags");
                case RadioAction.Penalty:
                    if (frame.RaceControl is not { Penalty: int penalty } control || !RaceReports.Fresh(control.ObservedAt, now))
                        return "当前处罚状态暂不可用。";
                    return penalty == 0 ? "当前没有待执行处罚。" : RaceAlertMonitor.PenaltyText(frame.Game, penalty).Replace("你收到新的处罚，", "");
                default: return "";
            }
        }
        return (frame.Origin == DataOrigin.Synthetic ? "【演示数据】" : "") + string.Concat(rule.Actions.Select(Read));
    }
}
