using System.Net;
using System.Text.Json;
using DeltaTR.Hardware.Moza;

internal static class MozaBridgeTests
{
    public static async Task Loopback()
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var time = DateTimeOffset.UtcNow;
        bool ready = false;
        await using var bridge = new PitHouseBridge(() => ready, () => time);
        var settings = new BridgeSettings(0, new string('A', 64));
        try { bridge.Start(settings); throw new Exception("missing installation accepted"); }
        catch (InvalidOperationException) { }
        ready = true;
        bridge.Update(new("reply", "左前胎温八十六度", 1)); bridge.Start(settings);
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false });
        async Task<HttpResponseMessage> Get(string key, string query = "protocol=4&field=DeltaTRRadio&session=&ack=0", string? origin = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{bridge.Port}/v1/screen?{query}");
            request.Headers.Add("X-DeltaTR-Key", key);
            if (origin is not null) request.Headers.Add("Origin", origin);
            return await client.SendAsync(request);
        }
        using (var bad = await Get(new string('B', 64))) Check(bad.StatusCode == HttpStatusCode.Forbidden, "reject wrong authentication");
        using (var browser = await Get(settings.Key, origin: "https://example.com")) Check(browser.StatusCode == HttpStatusCode.Forbidden, "reject browser origin");
        using (var legacy = await Get(settings.Key, "session=&ack=0")) Check(legacy.StatusCode == HttpStatusCode.Conflict, "old loaded QML must never receive a writable answer");
        Check(bridge.Status.Contains("旧版桥接"), "loaded legacy client shows restart guidance");
        using (var wrongField = await Get(settings.Key, "protocol=4&field=PlayerName")) Check(wrongField.StatusCode == HttpStatusCode.Conflict, "reject shared field");
        async Task<JsonElement> Read(string query = "session=&ack=0")
        {
            using var response = await Get(settings.Key, "protocol=4&field=DeltaTRRadio&" + query);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return json.RootElement.Clone();
        }
        bridge.Update(new("reply", "左前胎温八十六度", 1), "radio", 1);
        var first = await Read();
        Check(first.GetProperty("screenMode").GetString() == "radio", "dashboard request delivered independently of text");
        Check(first.GetProperty("version").GetInt32() == 4 && first.GetProperty("field").GetString() == PitHouseBridgeInstaller.Field, "dedicated field protocol");
        Check(first.GetProperty("rows")[0].GetString() == "reply|102|左前胎温八十六度", "actual Chinese page delivered");
        string session = first.GetProperty("session").GetString()!;
        long seq = first.GetProperty("seq").GetInt64();
        Check(bridge.Status.Contains("等待消息提交确认"), "poll is not an acknowledgement");
        await Read($"session=invalid&ack={seq}");
        Check(!bridge.Status.Contains("已接收"), "ignore ack from wrong session");
        await Read($"session={session}&ack={seq + 100}");
        Check(!bridge.Status.Contains("已接收"), "ignore future ack");
        await Read($"session={session}&ack={seq}");
        Check(bridge.Status.Contains("已接收"), "matching ack accepted");
        var same = await Read(); Check(same.GetProperty("seq").GetInt64() == seq, "heartbeat does not resubmit unchanged page");
        time += TimeSpan.FromSeconds(4);
        var stale = await Read(); Check(!stale.GetProperty("enabled").GetBoolean(), "stalled app cannot renew message");
        bridge.Update(new("reply", new string('胎', 120) + "第二页", 2));
        var page1 = await Read();
        time += TimeSpan.FromSeconds(8); bridge.Update(new("reply", new string('胎', 120) + "第二页", 2));
        var page2 = await Read();
        Check(page1.GetProperty("rows")[0].GetString()!.StartsWith("reply|14|") && page2.GetProperty("rows")[0].GetString() == "reply|102|第二页", "telemetry refresh preserves page clock");
        time += TimeSpan.FromSeconds(2); bridge.Update(new("reply", "比赛中的回答", 3));
        var racing = await Read();
        Check(racing.GetProperty("enabled").GetBoolean() && racing.GetProperty("allowReset").GetBoolean(), "dedicated answer and reset remain enabled");
        Check(racing.GetProperty("rows")[0].GetString() == "reply|102|比赛中的回答", "live answer remains fresh");
        var stopping = bridge.StopAsync();
        Check(!(await Read()).GetProperty("enabled").GetBoolean(), "graceful stop explicitly releases client");
        await stopping;
        bridge.Start(settings); bridge.Update(new("offline", "未连接游戏", 5));
        Check((await Read()).GetProperty("session").GetString() != session, "restart invalidates previous session");
    }
}
