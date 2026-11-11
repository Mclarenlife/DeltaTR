using System.Text.Json;
using System.Security.Cryptography;

namespace DeltaTR.Core;

/// <summary>HUD evidence must match the exact runtime context. Empty profiles never approve data.</summary>
public sealed record CalibrationProfile
{
    public string GameVersion { get; init; } = "";
    public string SharedMemoryVersion { get; init; } = "";
    public string CarModel { get; init; } = "";
    public string Track { get; init; } = "";
    public string Evidence { get; init; } = "";
    public string? EvidenceSha256 { get; init; }
    public Dictionary<string, string> VerifiedUnits { get; init; } = [];

    public bool Matches(string version, string sm, string car, string track) =>
        !string.IsNullOrWhiteSpace(Evidence) && !string.IsNullOrWhiteSpace(GameVersion) &&
        GameVersion == version && SharedMemoryVersion == sm && CarModel == car && Track == track;

    public static CalibrationProfile Load(string? path)
    {
        if (path is null) return new();
        var profile = JsonSerializer.Deserialize<CalibrationProfile>(File.ReadAllText(path), TelemetryJson.Options)
            ?? throw new InvalidDataException("校准文件不能为空。");
        if (profile.VerifiedUnits is null || profile.VerifiedUnits.Any(x => string.IsNullOrWhiteSpace(x.Value)))
            throw new InvalidDataException("verifiedUnits 必须为字段名称到规范单位的映射。");
        if (profile.EvidenceSha256 is not null)
        {
            if (string.IsNullOrWhiteSpace(profile.Evidence) || Path.GetFileName(profile.Evidence) != profile.Evidence)
                throw new InvalidDataException("校准包证据必须位于配置同目录。");
            var evidencePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, profile.Evidence);
            if (!File.Exists(evidencePath) || !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(evidencePath))),
                    profile.EvidenceSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("校准证据缺失或校验不匹配，配置未加载。");
        }
        return profile;
    }
}
