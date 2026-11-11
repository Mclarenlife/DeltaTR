using System.Text;

namespace DeltaTR.Telemetry.Simulators;

/// <summary>2026 SDK sessions may opt into UTF-8. Older sessions are ISO-8859-1.</summary>
internal static class IRacingSessionText
{
    internal static string Decode(byte[] bytes)
    {
        var end = Array.IndexOf(bytes, (byte)0);
        var span = bytes.AsSpan(0, end < 0 ? bytes.Length : end);
        var legacy = Encoding.Latin1.GetString(span);
        var inWeekend = false;
        foreach (var line in legacy.Split('\n'))
        {
            var text = line.TrimEnd('\r');
            if (text == "WeekendInfo:") { inWeekend = true; continue; }
            if (!inWeekend) continue;
            if (text.Length > 0 && !char.IsWhiteSpace(text[0])) break;
            var field = text.Trim();
            if (!field.StartsWith("Encoding:", StringComparison.Ordinal)) continue;
            var value = field["Encoding:".Length..].Split('#')[0].Trim().Trim('"', '\'');
            return value is "UTF8" or "UTF-8" ? Encoding.UTF8.GetString(span) : legacy;
        }
        return legacy;
    }
}
