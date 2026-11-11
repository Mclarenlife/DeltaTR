using System.Net;
using System.Text.Json;
using DeltaTR.Core;
using DeltaTR.Engineer;
using DeltaTR.Telemetry.ACC;

internal static class StrategyTests
{
    private static void Require(bool condition, string reason)
    { if (!condition) throw new InvalidOperationException(reason); }

    internal static void Sampling()
    {
        var tracker = new StrategyTelemetry();
        var start = DateTimeOffset.UtcNow;
        RaceSnapshot Frame(int ms, int laps, double fuel, bool pit = false)
        {
            var frame = SyntheticAdapter.Create(0, 0, start.AddMilliseconds(ms));
            frame.Fields["completedLaps"] = frame.Fields["completedLaps"] with { Value = laps };
            frame.Fields["fuel"] = frame.Fields["fuel"] with { Value = fuel };
            frame.Fields["inPitLane"] = new(pit ? 1 : 0, "boolean", frame.CapturedAt, DataQuality.Valid, "fixture", "");
            return frame;
        }
        void Observe(int ms, int laps, double fuel, bool pit = false)
        {
            var frame = Frame(ms, laps, fuel, pit);
            tracker.Observe(frame, frame.CapturedAt);
        }
        Observe(0, 0, 45); Observe(1000, 1, 42);
        Require(tracker.Laps.Count == 0, "Wait for last-lap timing to catch up");
        Observe(1800, 1, 42);
        Require(tracker.Laps.Count == 1 && tracker.Laps[0].FuelUsedLitres == null, "First sampled lap lacks a full fuel baseline");
        Observe(2000, 2, 40); Observe(2800, 2, 40);
        Require(tracker.Laps.Count == 2 && tracker.Laps[1].FuelUsedLitres == 2, "Measured per-lap fuel");
        Observe(3000, 3, 39, true); Observe(3800, 3, 44);
        Require(tracker.Laps.Count == 2, "Pit/refuel lap excluded");
        Observe(4000, 4, 42); Observe(4800, 4, 42);
        Require(tracker.Laps.Count == 3, "Resume sampling after the pit lap");
        Observe(5000, 7, 35); Observe(5800, 7, 35);
        Require(tracker.Laps.Count == 3, "Skipped lap counters must not fabricate samples");
        var next = Frame(6000, 0, 45) with { Game = RacingGame.LMU, SessionId = "next" };
        tracker.Observe(next, next.CapturedAt);
        Require(tracker.Laps.Count == 0, "Session/game samples isolated");
        tracker.Observe(null, next.CapturedAt);
        Require(tracker.Laps.Count == 0, "Disconnect clears evidence");
    }

    internal static async Task Request()
    {
        using var handler = new Handler();
        using var client = new HttpClient(handler);
        var now = DateTimeOffset.UtcNow;
        var frame = SyntheticAdapter.Create(0, 0, now, game: RacingGame.LMU) with
        {
            Native = new([], key => key == "LMU.Wheel.mWear.FL"
                ? new(.85, "fraction", now, DataQuality.Valid, "LMU", "") : null, "fixture"),
            LocalFields = new Dictionary<string, Measurement> { ["driver"] =
                new(null, "", now, DataQuality.Valid, "local", "") { TextValue = "PRIVATE_DRIVER_NAME" } }
        };
        var profile = new StrategyProfile { ReplyLength = ReplyLength.Detailed,
            Approach = StrategyApproach.Conservative, Instructions = "先报告结论，叫我车手。", ShowTyres = false };
        StrategyLap[] laps = [new(1, 92, 2.1, now.AddSeconds(-1))];
        var result = await AiRadio.AskAsync(AiRadio.Endpoint("http://localhost:9999/v1"), "test", "",
            StrategyProfile.Question(StrategyTopic.Pace), frame, [], CancellationToken.None, client, profile, laps);
        Require(result == "测试分析", "AI response");
        using (var doc = JsonDocument.Parse(handler.Body))
        {
            var messages = doc.RootElement.GetProperty("messages");
            string system = messages[0].GetProperty("content").GetString()!;
            string user = messages[1].GetProperty("content").GetString()!;
            Require(system.Contains("优先完赛") && system.Contains("不同方案"), "Saved style and approach reach AI");
            Require(user.Contains("先报告结论") && user.Contains("FuelUsedLitres") && user.Contains("LMU.Wheel.mWear.FL"), "Preferences and live strategy evidence");
            Require(!user.Contains("PRIVATE_DRIVER_NAME"), "Local identity is not strategy evidence");
        }
        await AiRadio.AskAsync(AiRadio.Endpoint("http://localhost:9999/v1"), "test", "", "分析",
            frame with { Phase = SessionPhase.Paused }, [], CancellationToken.None, client, profile, laps);
        using var paused = JsonDocument.Parse(handler.Body);
        var pausedInput = paused.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
        Require(pausedInput.Contains("\"recentLaps\":null") && !pausedInput.Contains("LMU.Wheel.mWear.FL"), "Paused evidence is withheld");
        bool rejected = false;
        try { (profile with { Instructions = new string('x', 2001) }).Validate(); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "Preference length validation");
    }

    private sealed class Handler : HttpMessageHandler
    {
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Body = await request.Content!.ReadAsStringAsync(token);
            return new(HttpStatusCode.OK) { Content = new StringContent("""{"choices":[{"message":{"content":"测试分析"}}]}""") };
        }
    }
}
