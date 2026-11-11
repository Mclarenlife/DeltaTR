using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DeltaTR.Core;

namespace DeltaTR.Engineer;

public sealed record RadioMessage(string role, string content);

public static class AiRadio
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        Timeout = TimeSpan.FromSeconds(35), MaxResponseContentBufferSize = 1024 * 1024
    };

    public static string? AfterWakeWord(string text, string wakeWord)
    {
        if (string.IsNullOrWhiteSpace(wakeWord)) return null;
        var phrase = text.TrimStart();
        var consumed = 0;
        foreach (var letter in wakeWord.Trim())
        {
            if (char.IsWhiteSpace(letter)) continue;
            while (consumed < phrase.Length && char.IsWhiteSpace(phrase[consumed])) consumed++;
            if (consumed >= phrase.Length || char.ToUpperInvariant(phrase[consumed++]) != char.ToUpperInvariant(letter)) return null;
        }
        if (consumed < phrase.Length && char.IsAsciiLetterOrDigit(wakeWord.Trim()[^1]) && char.IsAsciiLetterOrDigit(phrase[consumed]))
            return null;
        while (consumed < phrase.Length && (char.IsPunctuation(phrase[consumed]) || char.IsWhiteSpace(phrase[consumed]))) consumed++;
        return phrase[consumed..].TrimEnd();
    }

    public static Uri Endpoint(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new ArgumentException("请输入 HTTPS API 地址，本机服务可使用 HTTP。");
        var path = uri.AbsoluteUri.TrimEnd('/');
        return path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? new Uri(path) : new Uri(path + "/chat/completions");
    }

    public static async Task<string> AskAsync(Uri endpoint, string model, string key, string question,
        RaceSnapshot? snapshot, IReadOnlyList<RadioMessage> history, CancellationToken token, HttpClient? client = null,
        StrategyProfile? preferences = null, IReadOnlyList<StrategyLap>? recentLaps = null)
    {
        var now = DateTimeOffset.UtcNow;
        var fresh = snapshot is { Phase: SessionPhase.Live } && snapshot.CapturedAt <= now && now - snapshot.CapturedAt <= Metrics.MaxAge;
        var telemetry = fresh ? snapshot!.Fields.Where(p => p.Value.IsUsable(now, Metrics.MaxAge))
            .ToDictionary(p => p.Key, p => new { Value = (object?)p.Value.TextValue ?? p.Value.Value, p.Value.Unit }) : null;
        // Provide rank and driving observations to the API without transmitting the local entry list or driver names.
        var race = snapshot?.Broadcast is { Driving: true } feed && snapshot.Phase is not (SessionPhase.Paused or SessionPhase.Replay)
            ? feed.Fields.Where(p => p.Value.IsUsable(now, Metrics.MaxAge) &&
                (p.Key.EndsWith(".motion") || p.Key.EndsWith(".position") || p.Key.EndsWith(".last") || p.Key.EndsWith(".best")))
                .ToDictionary(p => p.Key, p => p.Value.TextValue) : null;
        var profile = preferences ?? new StrategyProfile();
        var strategyData = fresh ? StrategyTelemetry.ExtraReadings(snapshot!, now).ToDictionary(p => p.Key,
            p => new { p.Value.Value, p.Value.TextValue, p.Value.Unit, quality = p.Value.Quality.ToString() }) : null;
        var sampledLaps = fresh ? recentLaps?.Where(l => l.ObservedAt <= now && now - l.ObservedAt <= TimeSpan.FromHours(1)).TakeLast(6).ToArray() : null;
        var state = JsonSerializer.Serialize(new { game = snapshot?.Game.Display(), source = snapshot?.Origin.ToString(), fresh, telemetry, race,
            strategyData, recentLaps = sampledLaps });
        var messages = new List<RadioMessage> { new("system",
            "你是赛车工程师。用适合语音收听的中文回答，长度遵循车手偏好。每次以最新遥测为准，历史回答只作对话背景。使用最新遥测中随数值给出的单位作答。只使用提供的数据，缺失数值不得补造。缺少油耗、剩余圈数、进站窗口等依据时说明缺什么，不能编造进站圈数。Synthetic 是演示数据。iRacing 的 pitTemp、pitTread 是上次测量值，coldPressure 是冷胎压设定。圈速样本可能受交通影响，不能仅据此断言轮胎退化。每圈采样油耗是观测值，不能保证后续每圈相同。提供的 Estimated 数据必须说明是估算。单位为原始值的字段不用于时间或圈数计算。驾驶偏好不能用于补造遥测。"
            + profile.Prompt()) };
        messages.AddRange(history.TakeLast(6));
        messages.Add(new("user", $"当前遥测 {state}\n车手对话偏好 {profile.Instructions}\n车手问题 {question}"));
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(new { model, messages, stream = false });
        using var response = await (client ?? Client).SendAsync(request, token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"API 返回 HTTP {(int)response.StatusCode}，请检查地址、密钥、模型或额度。");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!json.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0
            || !choices[0].TryGetProperty("message", out var message) || !message.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(content.GetString()))
            throw new InvalidOperationException("API 未返回有效的对话内容，请核对接口协议与模型。");
        return content.GetString()!.Trim();
    }
}
