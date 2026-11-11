using DeltaTR.Core;

namespace DeltaTR.Engineer;

[Flags]
public enum RaceAlertKind
{
    None = 0, Yellow = 1, Position = 2, Penalty = 4, Blue = 8, Green = 16,
    Pit = 32, Lap = 64, Session = 128, All = 255
}

public sealed class RaceAlertMonitor
{
    private sealed record Pending(string Text, DateTimeOffset At, int? Rank = null, int? OpponentId = null);
    private readonly Dictionary<RaceAlertKind, Pending> pending = [];
    private readonly List<RaceNotice> occurrences = [];
    public IReadOnlyList<RaceNotice> Occurrences => occurrences;
    private (RacingGame, DataOrigin, string)? session;
    private DateTimeOffset lastFrame, candidateSince, lastPosition;
    private bool? previousYellow, previousBlue, previousPit, previousRacing, previousFinished;
    private int? previousPenalty, position, candidate, rankBeforeQueue, previousLap;
    private bool raceStarted;
    private RaceTraffic? rankedTraffic;
    private RaceSnapshot? latest;
    private DateTimeOffset observedNow;
    private static readonly RaceAlertKind[] Order = [RaceAlertKind.Yellow, RaceAlertKind.Penalty,
        RaceAlertKind.Blue, RaceAlertKind.Green, RaceAlertKind.Position, RaceAlertKind.Session, RaceAlertKind.Pit, RaceAlertKind.Lap];

    public void ClearPending() { pending.Clear(); rankBeforeQueue = null; }

    private void Reset()
    {
        ClearPending();
        previousYellow = previousBlue = previousPit = previousRacing = previousFinished = null;
        previousPenalty = position = candidate = previousLap = null;
        rankedTraffic = null; raceStarted = false; session = null;
    }

    public void Observe(RaceSnapshot? frame, DateTimeOffset now, RaceAlertKind enabled)
    {
        occurrences.Clear(); latest = frame; observedNow = now;
        if (frame is not { Phase: SessionPhase.Live } || !RaceReports.Fresh(frame.CapturedAt, now))
        {
            // A brief synchronisation frame must not erase the previous valid race position.
            if (frame?.Phase == SessionPhase.Connecting && session?.Item3 == frame.SessionId &&
                now - lastFrame <= Metrics.MaxAge) return;
            Reset(); return;
        }
        var identity = (frame.Game, frame.Origin, frame.SessionId);
        if (session != identity || !RaceReports.Fresh(lastFrame, now)) Reset();
        session = identity; lastFrame = now;
        foreach (var kind in Order)
            if (!enabled.HasFlag(kind) || pending.TryGetValue(kind, out var alert) && now - alert.At > TimeSpan.FromSeconds(30))
                Drop(kind);
        void Emit(RaceAlertKind kind, RadioTrigger trigger, string text)
        {
            occurrences.Add(new(trigger, text));
            if (enabled.HasFlag(kind)) pending[kind] = new(text, now);
        }
        var control = frame.RaceControl;
        if (control != null && !RaceReports.Fresh(control.ObservedAt, now)) control = null;
        var yellow = control?.Yellow;
        if (yellow != true) Drop(RaceAlertKind.Yellow);
        if (yellow != false) Drop(RaceAlertKind.Green);
        if (yellow == true && previousYellow == false)
            Emit(RaceAlertKind.Yellow, RadioTrigger.Yellow, frame.Game == RacingGame.LMU ? "全场黄旗，注意减速。" : "黄旗，注意减速。");
        if (yellow == false && previousYellow == true)
            Emit(RaceAlertKind.Green, RadioTrigger.Green, "黄旗已解除。");
        previousYellow = yellow;

        if (control?.Blue != true) Drop(RaceAlertKind.Blue);
        if (control?.Blue == true && previousBlue == false)
            Emit(RaceAlertKind.Blue, RadioTrigger.Blue, "蓝旗，留意后方快车。");
        previousBlue = control?.Blue;

        var penalty = control?.Penalty;
        if (penalty is null or <= 0) Drop(RaceAlertKind.Penalty);
        if (penalty > 0 && previousPenalty is int before && before != penalty)
        {
            Drop(RaceAlertKind.Penalty);
            if (frame.Game == RacingGame.ACC || penalty > before)
                Emit(RaceAlertKind.Penalty, RadioTrigger.Penalty, PenaltyText(frame.Game, penalty.Value));
        }
        if (penalty == 0 && previousPenalty > 0)
            occurrences.Add(new(RadioTrigger.PenaltyCleared, "当前处罚已清零。"));
        previousPenalty = penalty;

        var traffic = frame.Traffic;
        if (traffic != null && !RaceReports.Fresh(traffic.ObservedAt, now)) traffic = null;
        if (traffic?.Racing == true)
        {
            if (previousRacing == false && !raceStarted)
                Emit(RaceAlertKind.Session, RadioTrigger.RaceStart, "比赛开始。" + PositionText(frame, now));
            raceStarted = true;
        }
        if (traffic?.Finished == true && previousFinished == false)
            Emit(RaceAlertKind.Session, RadioTrigger.RaceFinish, "比赛结束。" + PositionText(frame, now));
        previousRacing = traffic?.Racing; previousFinished = traffic?.Finished;

        bool? pit = RaceReports.Number(frame, "inPitLane", now) is double inPit ? inPit == 1 : null;
        if (pit.HasValue && previousPit.HasValue && pit != previousPit)
            Emit(RaceAlertKind.Pit, pit.Value ? RadioTrigger.PitEntry : RadioTrigger.PitExit,
                pit.Value ? "已进入维修区，注意限速。" : "已离开维修区，注意汇入车流。");
        previousPit = pit;
        int? lap = RaceReports.Number(frame, "completedLaps", now) is double laps ? (int)laps : null;
        if (lap > previousLap) Emit(RaceAlertKind.Lap, RadioTrigger.LapCompleted, $"已完成第 {lap} 圈。");
        previousLap = lap;

        var reading = RaceReports.Position(frame, now);
        if (reading?.Value is not double number)
        {
            candidate = null;
            if (now - lastPosition > TimeSpan.FromSeconds(3)) { position = null; rankedTraffic = null; Drop(RaceAlertKind.Position); }
            return;
        }
        lastPosition = now;
        int rank = (int)number;
        if (position == null) { position = rank; rankedTraffic = traffic; }
        if (candidate != rank) { candidate = rank; candidateSince = reading.ObservedAt; }
        if (rank == position)
        {
            rankedTraffic = traffic;
            return;
        }
        if (reading.ObservedAt - candidateSince < TimeSpan.FromMilliseconds(500)) return;
        int previous = position.Value;
        int original = rankBeforeQueue ?? previous;
        int? opponent = FindOpponent(rankedTraffic, traffic, previous, rank, now);
        string text = $"排名{(rank < previous ? "升至" : "降至")}第 {rank} 名。";
        occurrences.Add(new(rank < previous ? RadioTrigger.PositionGained : RadioTrigger.PositionLost, text));
        position = rank; rankedTraffic = traffic;
        if (rank == original || !enabled.HasFlag(RaceAlertKind.Position)) Drop(RaceAlertKind.Position);
        else
        {
            rankBeforeQueue = original;
            pending[RaceAlertKind.Position] = new($"排名{(rank < original ? "升至" : "降至")}第 {rank} 名。", now, rank, opponent);
        }
    }

    private static int? FindOpponent(RaceTraffic? before, RaceTraffic? after, int oldRank, int newRank, DateTimeOffset now)
    {
        if (before == null || after?.Player is not { } player || player.InPits || before.Player?.InPits != false ||
            !RaceReports.Fresh(before.ObservedAt, now)) return null;
        var crossed = after.Cars.Where(car => car.Id != player.Id && !car.InPits && RaceReports.Fresh(car.ObservedAt, now) &&
            before.Cars.Any(old => old.Id == car.Id && !old.InPits &&
                (newRank > oldRank ? old.Position > oldRank && car.Position < newRank : old.Position < oldRank && car.Position > newRank)))
            .Take(2).ToArray();
        return crossed.Length == 1 ? crossed[0].Id : null;
    }

    public string? TakeReady(bool imperial = false, RaceReports? reports = null)
    {
        if (latest?.Phase != SessionPhase.Live) return null;
        var messages = new List<string>();
        foreach (var kind in Order)
        {
            if (kind == RaceAlertKind.Position && candidate != position) continue;
            if (!pending.TryGetValue(kind, out var item)) continue;
            string text = item.Text;
            if (item.OpponentId is int id && latest.Traffic?.Cars.FirstOrDefault(c => c.Id == id) is { } opponent &&
                RaceReports.Fresh(opponent.ObservedAt, observedNow))
            {
                string name = string.IsNullOrWhiteSpace(opponent.Name) ? $"车辆 {id}" : opponent.Name;
                bool ahead = opponent.Position < item.Rank;
                text += $"{name}当前在你{(ahead ? "前" : "后")}方。";
                if (!ahead) text += RaceReports.SpeedDifference(latest, opponent, observedNow, imperial);
            }
            if (kind == RaceAlertKind.Position && item.Rank > rankBeforeQueue)
                text += (reports ?? new RaceReports()).Gap(latest, observedNow, false);
            messages.Add(text); Drop(kind);
        }
        return messages.Count == 0 ? null : (session?.Item2 == DataOrigin.Synthetic ? "【演示数据】" : "") + string.Concat(messages);
    }

    public static string PenaltyText(RacingGame game, int penalty) => game switch
    {
        RacingGame.LMU => $"你收到新的处罚，当前有 {penalty} 项待执行。",
        RacingGame.IRacing when penalty == 2 => "你已被取消比赛资格。",
        RacingGame.IRacing => "你收到黑旗，请查看赛事控制通知。",
        _ => "你收到新的处罚，请查看赛事控制通知。"
    };
    private static string PositionText(RaceSnapshot frame, DateTimeOffset now) =>
        RaceReports.Position(frame, now)?.Value is double rank ? $"目前第 {rank:0} 名。" : "";
    private void Drop(RaceAlertKind kind) { pending.Remove(kind); if (kind == RaceAlertKind.Position) rankBeforeQueue = null; }
}
