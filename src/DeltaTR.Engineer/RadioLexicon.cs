using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using DeltaTR.Core;

namespace DeltaTR.Engineer;

public sealed record RadioRequest(string? Metric, string? WheelToClarify = null, TyreLimit? Limit = null);

public sealed record TyreLimit(bool Above, double Value, string Unit)
{
    public EngineerReply Evaluate(EngineerReply reply, RaceSnapshot? state)
    {
        if (!reply.HasVerifiedData || state is null) return reply;
        var passed = new List<string>();
        var inspected = new List<string>();
        string unit = Unit;
        foreach (string key in reply.EvidenceFields)
        {
            var reading = state.Reading(key);
            if (reading?.Value is not double value || !double.IsFinite(value))
                return new("缺少有效读数，暂时无法比较。", false, []);
            if (unit.Length == 0) unit = reading.Unit;
            if (reading.Unit == "°F" && unit == "°C") value = (value - 32) / 1.8;
            else if (reading.Unit == "°C" && unit == "°F") value = value * 1.8 + 32;
            else if (unit != reading.Unit) return new("比较单位与遥测单位不符。", false, []);
            string wheel = key.Split('.')[^1] switch { "FL" => "左前", "FR" => "右前", "RL" => "左后", "RR" => "右后", _ => key };
            inspected.Add(wheel);
            if (Above ? value > Value : value < Value) passed.Add(wheel);
        }
        string condition = (Above ? "高于" : "低于") + Value.ToString("0.###", CultureInfo.InvariantCulture) + " " + unit;
        string result = passed.Count == 0
            ? (inspected.Count == 1 ? inspected[0] + "轮未" : "四轮均未") + condition
            : string.Join("、", passed) + "轮" + condition;
        return reply with { Text = reply.Text + result + "。" };
    }
}

/// <summary>Spoken phrases and their meaning for one game's current vocabulary.</summary>
public sealed partial class RadioLexicon : ReadOnlyCollection<string>
{
    private readonly IReadOnlyDictionary<string, string> aliases;
    private readonly HashSet<string> enabled;
    internal RadioLexicon(string[] phrases, IReadOnlyDictionary<string, string> aliases, IEnumerable<string> enabled)
        : base(phrases) { this.aliases = aliases; this.enabled = enabled.ToHashSet(); }

    [GeneratedRegex(@"^(?:麻烦你?|请你?|能不能|能否|可以|帮我|给我|我想知道|告诉我|报一下|查一下|看一下|看下|看看|说一下|现在|当前|目前|此刻)")]
    private static partial Regex LeadIn();
    [GeneratedRegex(@"^(?<wheel>左前|右前|左后|右后|四个|四条|四只|四轮|每个|每条)?(?:轮胎|轮子|轮|胎)(?:现在|目前|当前)?(?:的)?(?:各自|分别|各|都)?(?:有)?(?<what>温度|胎温|温|压力|胎压|压|多热|几度|多少度|多少个?psi|多少磅|多少)?(?<tail>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex Tyre();
    [GeneratedRegex(@"^(?:有没有|是否|有)?(?<op>超过|高于|大于|低于|小于)(?<number>[0-9]{1,3}(?:[.点][0-9]{1,3})?|[零〇一二两三四五六七八九十]{1,4}(?:点[零〇一二三四五六七八九]{1,3})?)(?<unit>psi|磅|摄氏度|华氏度|度|°c|°f)?(?:了)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Comparison();

    public RadioRequest? Interpret(string heard)
    {
        if (heard.Length > 160) return null;
        string input = string.Concat(heard.Trim().Where(c => !char.IsWhiteSpace(c))).TrimEnd('。','？','?','！','!','.');
        input = input.TrimEnd('吗','呢','啊','呀','吧');
        string body = input;
        // Prefer the longest intact alias when several conversational lead-ins precede it.
        while (true)
        {
            if (aliases.TryGetValue(body, out var known)) return new(known);
            var lead = LeadIn().Match(body);
            if (!lead.Success) break;
            body = body[lead.Length..].TrimStart('，', ',');
        }
        var corrected = RepairTyreTerms(body);
        if (corrected != body && aliases.TryGetValue(corrected, out var tyreAlias)) return new(tyreAlias);
        body = corrected;
        var match = Tyre().Match(body);
        if (!match.Success) return null;
        string wheel = match.Groups["wheel"].Value;
        string suffix = wheel switch { "左前" => ".FL", "右前" => ".FR", "左后" => ".RL", "右后" => ".RR", _ => "" };
        string what = match.Groups["what"].Value.ToLowerInvariant();
        string tail = match.Groups["tail"].Value;
        string metric = what switch
        {
            "温" or "温度" or "胎温" or "多热" or "几度" or "多少度" => "temperature",
            "压力" or "压" or "胎压" or "多少psi" or "多少个psi" or "多少磅" => "pressure",
            _ => ""
        };
        if (metric.Length == 0)
        {
            if (what == "多少" && tail.Length == 0 && enabled.Contains("pressure" + suffix) && enabled.Contains("temperature" + suffix))
                return new(null, suffix.Length == 0 ? "四轮" : wheel);
            return null;
        }
        if (!enabled.Contains(metric + suffix)) return null;
        tail = tail.ToLowerInvariant();
        if (tail is "" or "多少" or "是多少" or "多少度" or "是多少度" or "怎么样" or "如何" or "是多少psi" or "多少psi" or "有多高" or "多高")
        {
            if (tail.Contains("度") && metric != "temperature" || tail.Contains("psi") && metric != "pressure") return null;
            return new(metric + suffix);
        }
        var comparison = Comparison().Match(tail);
        if (!comparison.Success || !TryNumber(comparison.Groups["number"].Value, out double threshold)) return null;
        string unit = comparison.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "摄氏度" or "°c" => "°C", "华氏度" or "°f" => "°F", "psi" or "磅" => "psi", var other => other
        };
        if ((unit is "°C" or "°F" or "度") && metric != "temperature" || unit == "psi" && metric != "pressure") return null;
        return new(metric + suffix, Limit: new(comparison.Groups["op"].Value is "高于" or "大于" or "超过", threshold, unit == "度" ? "" : unit));
    }

    public static string RepairTyreTerms(string text) => text.Replace("轮台", "轮胎").Replace("轮抬", "轮胎")
        .Replace("台温", "胎温").Replace("抬温", "胎温").Replace("台压", "胎压").Replace("抬压", "胎压");

    private static bool TryNumber(string number, out double result)
    {
        string digits = number.Replace('点','.').Replace('〇','零').Replace('两','二');
        for (int i = 0; i < 10; i++) digits = digits.Replace("零一二三四五六七八九"[i], (char)('0' + i));
        if (digits.Contains('十'))
        {
            var tens = Regex.Match(digits, @"^([1-9]?)十([1-9]?)(\.[0-9]+)?$");
            if (!tens.Success) { result = 0; return false; }
            digits = (tens.Groups[1].Length == 0 ? "1" : tens.Groups[1].Value) +
                     (tens.Groups[2].Length == 0 ? "0" : tens.Groups[2].Value) + tens.Groups[3].Value;
        }
        return double.TryParse(digits, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out result);
    }
}
