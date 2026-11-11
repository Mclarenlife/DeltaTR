using DeltaTR.Engineer;

namespace DeltaTR.Voice;

/// <summary>Repair a whole local command only if it has a single reading in the active vocabulary.</summary>
internal static class TelemetrySpeechMatcher
{
    // Whole racing terms, not interchangeable characters ("台" in "那台车" has its own meaning).
    private static readonly (string Heard, string Term)[] Confusions =
    [
        ("轮台", "轮胎"), ("轮抬", "轮胎"),
        ("台温", "胎温"), ("抬温", "胎温"), ("台压", "胎压"), ("抬压", "胎压"),
        ("近站", "进站")
    ];

    public static VoiceResult Match(string heard, string wakeWord, IReadOnlyList<string> vocabulary)
    {
        var phrase = new string(heard.Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c)).ToArray());
        var exact = vocabulary.FirstOrDefault(p => p.Equals(phrase, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return new(exact, $"听到“{heard}”");

        var key = Repair(phrase);
        if (key != phrase)
        {
            string? candidate = null;
            foreach (var option in vocabulary)
            {
                if (!key.Equals(Repair(option), StringComparison.OrdinalIgnoreCase)) continue;
                if (candidate is not null)
                    return new(null, $"听到“{heard}”，有多个相近问法，请重新讲话。");
                candidate = option;
            }
            if (candidate is not null && AiRadio.AfterWakeWord(candidate, wakeWord) is null)
                return new(candidate, $"听到“{heard}”，按词库问法“{candidate}”查询。");
        }
        return new(null, $"听到“{heard}”，未匹配词库或唤醒词。");
    }

    private static string Repair(string phrase)
    {
        for (int i = 1; i < phrase.Length; i++)
        {
            if (phrase[i] != phrase[i - 1] || phrase[i] is < '一' or > '鿿') continue;
            phrase = string.Concat(phrase.Where((c, n) => n == 0 || c != phrase[n - 1] || c is < '一' or > '鿿'));
            break;
        }
        foreach (var (heard, term) in Confusions)
            phrase = phrase.Replace(heard, term, StringComparison.Ordinal);
        return phrase;
    }
}
