using DeltaTR.Engineer;

namespace DeltaTR.Voice;

/// <summary>Repair a whole local command only if it has a single reading in the active vocabulary.</summary>
internal static class TelemetrySpeechMatcher
{
    private static readonly string[] QuestionEndings = ["多少", "几度", "几圈", "几秒", "怎么样"];
    // Keep decimal points and signs in threshold queries. Only speech separators can be dropped.
    internal static string Compact(string text) => string.Concat(text.Where(c => !char.IsWhiteSpace(c) &&
        c is not ('，' or ',' or '。' or '？' or '?' or '！' or '!'))).TrimEnd('.');

    // Used on partial results too. Only respell a whole telemetry question when its
    // corrected spelling resolves to the same field in this game's current vocabulary.
    internal static string DisplayQuestion(string text, string wakeWord, IReadOnlyList<string> vocabulary)
    {
        if (vocabulary is not RadioLexicon lexicon || AiRadio.AfterWakeWord(text, wakeWord) is not null) return text;
        var phrase = Compact(text);
        var corrected = RadioLexicon.RepairTyreTerms(phrase);
        if (corrected == phrase || AiRadio.AfterWakeWord(corrected, wakeWord) is not null) return text;
        return lexicon.Interpret(corrected) is { } target && lexicon.Interpret(phrase) == target ? corrected : text;
    }

    internal static string RemoveEcho(string phrase)
    {
        if (phrase.Length > 160) return phrase;
        // An entire identical question can occur twice in one PTT utterance.
        int half = phrase.Length / 2;
        if (half >= 2 && phrase.Length % 2 == 0 && phrase.AsSpan(0, half).SequenceEqual(phrase.AsSpan(half)))
            phrase = phrase[..half];
        // Restrict partial echoes to known question endings so e.g. 二点二二 stays intact.
        foreach (string ending in QuestionEndings)
        {
            while (phrase.EndsWith(ending + ending, StringComparison.Ordinal))
                phrase = phrase[..^ending.Length];
            if (phrase.EndsWith(ending + ending[^1], StringComparison.Ordinal))
                phrase = phrase[..^1];
        }
        return phrase;
    }

    public static VoiceResult Match(string heard, string wakeWord, IReadOnlyList<string> vocabulary)
    {
        var original = Compact(heard);
        var phrase = original;
        var exact = vocabulary.FirstOrDefault(p => p.Equals(phrase, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return new(DisplayQuestion(exact, wakeWord, vocabulary), $"听到“{heard}”");

        phrase = RemoveEcho(phrase);
        exact = phrase == original ? null : vocabulary.FirstOrDefault(p => p.Equals(phrase, StringComparison.OrdinalIgnoreCase));
        if (exact is not null && AiRadio.AfterWakeWord(exact, wakeWord) is null)
            return new(exact, $"听到“{heard}”，按词库问法“{exact}”查询。");
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
        if (vocabulary is RadioLexicon lexicon)
        {
            // Keep meaningful punctuation in conversational questions unless an echo was removed.
            string utterance = phrase == original ? heard : phrase;
            if (lexicon.Interpret(utterance) is { } request)
            {
                var corrected = RadioLexicon.RepairTyreTerms(utterance);
                // A custom alias can deliberately bind a homophone to a different metric.
                if (corrected != utterance && lexicon.Interpret(corrected) != request) corrected = utterance;
                if (AiRadio.AfterWakeWord(corrected, wakeWord) is null)
                    return new(corrected, corrected == heard ? $"听到“{heard}”" : $"听到“{heard}”，按“{corrected}”查询。");
            }
        }
        return new(null, $"听到“{heard}”，未匹配词库或唤醒词。");
    }

    private static string Repair(string phrase)
    {
        phrase = phrase.Replace("一一下", "一下");
        for (int i = 1; i < phrase.Length; i++)
        {
            if (phrase[i] != phrase[i - 1] || !CanStutter(phrase[i])) continue;
            phrase = string.Concat(phrase.Where((c, n) => n == 0 || c != phrase[n - 1] || !CanStutter(c)));
            break;
        }
        phrase = RadioLexicon.RepairTyreTerms(phrase).Replace("近站", "进站");
        // SenseVoice can render a trailing echoed 少 as 手. Restrict this near-sound
        // repair to an explicit tyre-temperature question, never strip arbitrary tails.
        if (phrase.EndsWith("胎温多少手", StringComparison.Ordinal) ||
            phrase.EndsWith("轮胎温度多少手", StringComparison.Ordinal))
            phrase = phrase[..^1];
        return phrase;
    }

    private static bool CanStutter(char c) => c is >= '一' and <= '鿿' && !"零一二两三四五六七八九十百千万".Contains(c);
}
