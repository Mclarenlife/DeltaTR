using System.Text.Json;

namespace DeltaTR.Core;

public sealed record IRacingVariableInfo(string Name, int Type, int Count, bool CountAsTime, string Description, string Unit);
public sealed record IRacingSessionPath(string Path, string Type);

/// <summary>Versioned reference only. Runtime values and types come exclusively from the connected simulator.</summary>
public static class IRacingReference
{
    private sealed record ReferenceData(IRacingVariableInfo[] Variables, IRacingSessionPath[] SessionPaths);
    private static readonly ReferenceData Data = Load();
    public static IReadOnlyList<IRacingVariableInfo> Variables => Data.Variables;
    public static IReadOnlyList<IRacingSessionPath> SessionPaths => Data.SessionPaths;
    public static IReadOnlyDictionary<string, IRacingVariableInfo> ByName { get; } =
        Data.Variables.ToDictionary(v => v.Name, StringComparer.Ordinal);
    private static ReferenceData Load()
    {
        using var stream = typeof(IRacingReference).Assembly.GetManifestResourceStream("DeltaTR.Core.Resources.IRacingReference.json")
            ?? throw new InvalidOperationException("Missing iRacing reference catalog.");
        return JsonSerializer.Deserialize<ReferenceData>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Invalid iRacing reference catalog.");
    }

    public static bool IncludeSessionField(string name) =>
        !name.Contains("Password", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("Token", StringComparison.OrdinalIgnoreCase) &&
        !name.EndsWith("UserID", StringComparison.OrdinalIgnoreCase) &&
        !name.EndsWith("CustID", StringComparison.OrdinalIgnoreCase) &&
        name is not ("PrivateMessage" or "TelemetryDiskFile");

    public static string Metadata(int type, int count, bool timeSamples, string unit, string description, bool live)
    {
        var kind = type switch { 0 => "char", 1 => "bool", 2 => "int32", 3 => "uint32 位掩码", 4 => "float32", 5 => "float64", _ => "未知类型" };
        var shape = count > 1 ? $"[{count}]" : "";
        return (live ? "本次会话已公布" : "离线参考，是否实时提供以游戏为准") +
            $" · {kind}{shape} · 原始单位：{(string.IsNullOrWhiteSpace(unit) ? "无" : unit)}" +
            (timeSamples ? " · 数组为时间子采样，不是车辆编号" : "") + "。" + description;
    }
}
