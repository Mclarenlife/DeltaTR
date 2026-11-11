using DeltaTR.Core;
using DeltaTR.Engineer;
using DeltaTR.Telemetry.ACC;

internal static class AutomationTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow;
    private static RaceSnapshot Frame(int milliseconds, int rank = 5, bool racing = true)
    {
        var now = Start.AddMilliseconds(milliseconds);
        var frame = SyntheticAdapter.Create(0, 0, now) with
        {
            Traffic = new(15, [
                new(10, 1, "头车", now, 180, false, 2.4),
                new(14, rank == 5 ? 6 : 5, "对手", now, 180, false, 2.21),
                new(15, rank, "本车", now, 160, false, 2.2)
            ], now, racing, false)
        };
        frame.Fields["position"] = frame.Fields["position"] with { Value = 5 }; // Deliberately slower than roster.
        return frame;
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    internal static void RankAndSpeed()
    {
        var monitor = new RaceAlertMonitor();
        var baseline = Frame(0);
        monitor.Observe(baseline, baseline.CapturedAt, RaceAlertKind.Position);
        // An unavailable scoring field for one poll used to discard the previous position.
        var missing = Frame(200) with { Traffic = null };
        missing.Fields.Remove("position");
        monitor.Observe(missing, missing.CapturedAt, RaceAlertKind.Position);
        var changed = Frame(400, 6);
        monitor.Observe(changed, changed.CapturedAt, RaceAlertKind.Position);
        var stable = Frame(1000, 6);
        monitor.Observe(stable, stable.CapturedAt, RaceAlertKind.Position);
        // Retain the event for a whole PTT/recognition turn.
        for (int ms = 2000; ms <= 16000; ms += 1000)
        {
            stable = Frame(ms, 6);
            monitor.Observe(stable, stable.CapturedAt, RaceAlertKind.Position);
        }
        stable.Fields["gapAhead"] = new(1280, "ms", stable.CapturedAt, DataQuality.Valid, "fixture", "");
        var text = monitor.TakeReady() ?? "";
        Require(text.Contains("降至第 6 名") && text.Contains("对手") && text.Contains("落后前车 1.3 秒") && !text.Contains("km/h"), "Lost rank or current rival speed");
        monitor.Observe(Frame(17000, 6), Start.AddSeconds(17), RaceAlertKind.Position);
        Require(monitor.TakeReady() == null, "Repeated overtake alert");
        var noSpeed = stable.Traffic!.Cars[1] with { SpeedKph = null };
        Require(RaceReports.SpeedDifference(stable, noSpeed, stable.CapturedAt, false).Contains("暂不可用"), "Invented missing rival speed");
        Require(RaceReports.SpeedDifference(stable, stable.Traffic.Cars[1], stable.CapturedAt, true).Contains("12.4 mph"), "Imperial speed delta");
    }

    internal static void EventsAndRules()
    {
        var monitor = new RaceAlertMonitor();
        var scheduler = new RadioAutomation();
        var reports = new RaceReports();
        var startRule = new AutomationRule { Game = RacingGame.ACC, Name = "起步", Trigger = RadioTrigger.RaceStart };
        var interval = new AutomationRule { Game = RacingGame.ACC, Name = "每两分钟", IntervalSeconds = 120,
            Actions = [RadioAction.Position, RadioAction.GapLeader, RadioAction.GapAhead] };
        var disabled = startRule with { Id = Guid.NewGuid(), Enabled = false };
        var otherGame = startRule with { Id = Guid.NewGuid(), Game = RacingGame.LMU };
        AutomationRule[] rules = [startRule, interval, disabled, otherGame];
        void Observe(int ms, bool racing)
        {
            var f = Frame(ms, racing: racing);
            monitor.Observe(f, f.CapturedAt, RaceAlertKind.None);
            scheduler.Observe(f, f.CapturedAt, rules, monitor.Occurrences);
        }
        Observe(0, false); Observe(1000, true);
        Require(scheduler.PendingCount == 1, "Race start once, game and enable filters");
        Require(scheduler.TakeReady(Frame(1000), Start.AddSeconds(1), reports, false)!.Contains("第 5 名"), "Start position action");
        Observe(2000, false); Observe(3000, true);
        Require(scheduler.PendingCount == 0, "Yellow/green restart must not trigger a second race start");
        for (int ms = 4000; ms <= 119000; ms += 1000) Observe(ms, true);
        Require(scheduler.PendingCount == 0, "Interval ran early");
        Observe(120000, true);
        var text = scheduler.TakeReady(Frame(120000), Start.AddSeconds(120), reports, false)!;
        Require(text.IndexOf("第 5 名", StringComparison.Ordinal) < text.IndexOf("头车", StringComparison.Ordinal) &&
            text.IndexOf("头车", StringComparison.Ordinal) < text.IndexOf("前车", StringComparison.Ordinal), "Action order");
        Require(text.Contains("12.4 秒") && text.Contains("1.8 秒"), "Gap actions");
        var paused = Frame(121000) with { Phase = SessionPhase.Paused };
        scheduler.Observe(paused, paused.CapturedAt, rules, []);
        scheduler.Observe(Frame(180000), Start.AddSeconds(180), rules, []);
        Require(scheduler.PendingCount == 0, "Pause incorrectly accumulated time");
        // A long gap and a new session must not replay missed intervals or a start signal.
        scheduler.Observe(Frame(600000) with { SessionId = "next" }, Start.AddSeconds(600), rules, []);
        Require(scheduler.PendingCount == 0, "Session reset");

    }

    internal static void Reports()
    {
        var reports = new RaceReports();
        RaceSnapshot Sample(int ms, double player, double leader)
        {
            var f = Frame(ms);
            f.Fields.Remove("gapLeader");
            return f with { Game = RacingGame.IRacing, Traffic = new(15, [
                new(10, 1, "头车", f.CapturedAt, null, false, leader),
                new(15, 5, "本车", f.CapturedAt, 160, false, player)
            ], f.CapturedAt, true, false, 5000) };
        }
        reports.Observe(Sample(0, 2, 2.01), Start);
        var f = reports.Observe(Sample(500, 2.005, 2.015), Start.AddMilliseconds(500))!;
        Require(f.Traffic!.Cars[0].EstimatedSpeed && Math.Abs(f.Traffic.Cars[0].SpeedKph!.Value - 180) < .001, "Estimated rival speed");
        f = reports.Observe(Sample(1000, 2.01, 2.02), Start.AddSeconds(1))!;
        Require(reports.Gap(f, f.CapturedAt, true).Contains("估计落后头车 1.0 秒"), "Historical crossing time gap");
        f = reports.Observe(Sample(1500, 2.02, 2.5), Start.AddMilliseconds(1500))!;
        Require(f.Traffic!.Cars[0].SpeedKph == null, "Teleport must not become a speed");
        Require(reports.Gap(f, f.CapturedAt, true).Contains("暂不可用"), "Teleport invalidates path");
        var lapped = Sample(2000, 2, 3.5);
        Require(reports.Gap(lapped, lapped.CapturedAt, true).Contains("圈"), "Lapped car reported as seconds");
        var cold = new AutomationRule { Game = RacingGame.IRacing, Name = "胎压", Actions = [RadioAction.TyrePressure, RadioAction.TyreTemperature] };
        var demo = SyntheticAdapter.Create(0, 0, Start, game: RacingGame.IRacing);
        var result = RadioAutomation.Render(cold, demo, Start, reports, false);
        Require(!result.Contains("未匹配") && result.Contains("psi") && result.Contains("°C"), "iRacing uses cold pressure and measured tyre channels");
    }

    internal static void RuleChanges()
    {
        var scheduler = new RadioAutomation();
        var rule = new AutomationRule { Name = "测试", IntervalSeconds = 10 };
        for (int ms = 0; ms <= 10000; ms += 1000) scheduler.Observe(Frame(ms), Start.AddMilliseconds(ms), [rule], []);
        Require(scheduler.PendingCount == 1, "Interval schedule");
        scheduler.Observe(Frame(11000), Start.AddSeconds(11), [rule with { Enabled = false }], []);
        Require(scheduler.PendingCount == 0, "Disable must discard queued automation");
        scheduler.Observe(Frame(12000), Start.AddSeconds(12), [rule], []);
        scheduler.Observe(Frame(13000), Start.AddSeconds(13), [rule with { IntervalSeconds = 20 }], []);
        for (int ms = 14000; ms <= 32000; ms += 1000) scheduler.Observe(Frame(ms), Start.AddMilliseconds(ms), [rule with { IntervalSeconds = 20 }], []);
        Require(scheduler.PendingCount == 0, "Editing resets the schedule");
        scheduler.Observe(Frame(33000), Start.AddSeconds(33), [rule with { IntervalSeconds = 20 }], []);
        Require(scheduler.PendingCount == 1, "Edited interval");
        scheduler.ClearPending(); Require(scheduler.PendingCount == 0, "Silence cancels pending sequence");
        bool rejected = false;
        try { (rule with { Actions = [] }).Validate(); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Empty automation must be rejected");
    }
}
