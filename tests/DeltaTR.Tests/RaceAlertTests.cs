using DeltaTR.Core;
using DeltaTR.Engineer;
using DeltaTR.Telemetry.ACC;

internal static class RaceAlertTests
{
    internal static void Run(RacingGame game)
    {
        var monitor = new RaceAlertMonitor();
        var start = DateTimeOffset.UtcNow;
        void Check(bool condition, string reason)
        {
            if (!condition) throw new InvalidOperationException(reason);
        }
        void Frame(int ms, bool yellow = false, int penalty = 0, int rank = 5,
            RaceAlertKind enabled = RaceAlertKind.Yellow | RaceAlertKind.Position | RaceAlertKind.Penalty, SessionPhase phase = SessionPhase.Live, string session = "a")
        {
            var time = start.AddMilliseconds(ms);
            var frame = SyntheticAdapter.Create(ms, ms, time, session, game) with { RaceControl = new(yellow, penalty, time), Phase = phase, Traffic = null };
            frame.Fields["position"] = frame.Fields["position"] with { Value = rank };
            monitor.Observe(frame,time,enabled);
        }
        Frame(0,true,1);
        Check(monitor.TakeReady() == null, "Joining mid-event must be silent");
        Frame(100);
        Frame(200,true,1,4);
        Frame(400,true,1,4);
        Frame(1000,true,1,4);
        var message = monitor.TakeReady();
        Check(message != null && message.StartsWith("【演示数据】黄旗") || message?.StartsWith("【演示数据】全场黄旗") == true,
            "Yellow should lead combined alerts");
        Check(message!.Contains("第 4 名") && (message.Contains("处罚") || message.Contains("黑旗")), "Missing simultaneous event");
        Frame(1100,true,1,4);
        Check(monitor.TakeReady() == null,"Repeated packet should be silent");
        Frame(1200);
        Frame(1300,true,0,4, RaceAlertKind.Penalty);
        Frame(1400,true,0,4);
        Check(monitor.TakeReady() == null,"Re-enabling must not replay an active flag or a transient rank");
        Frame(1500,false,2,4);
        Frame(1600,true,2,4);
        Frame(1700,false,0,4);
        Check(monitor.TakeReady() == null,"Served penalty and cleared yellow should drop while driver is busy");
        Frame(1900,false,2,4);
        monitor.TakeReady();
        Frame(2000,false,1,4);
        Check((monitor.TakeReady() != null) == (game == RacingGame.ACC), "Penalty decrease semantics differ per simulator");
        Frame(2100,rank:3);
        Frame(2900,rank:3);
        Frame(3100,rank:4);
        Check(monitor.TakeReady() == null,"Pending rank should wait during a new rank transition");
        Frame(3900,rank:4);
        Check(monitor.TakeReady() == null,"Reversed overtake should not be announced after a busy turn");
        Frame(4000,phase:SessionPhase.Paused);
        Frame(4100,true,1,7);
        Frame(4200,false,0,2,session:"b");
        Frame(7300,true,1,3,session:"b");
        Check(monitor.TakeReady() == null,"Resume, new session and stale gaps must establish fresh baselines");
        Frame(7400,session:"b");
        Frame(7500,true,session:"b");
        for (int ms=8500;ms<40000;ms+=1000) Frame(ms,true,session:"b");
        Check(monitor.TakeReady() == null,"An old queued message must expire");
    }
}
