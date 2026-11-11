using System.Security.Cryptography;
using System.Text.Json;
using DeltaTR.Core;
using DeltaTR.Core.Validation;

namespace DeltaTR.Storage;

public static class CalibrationBundle
{
    public static async Task SaveAsync(CalibrationDraft draft, string profilePath, string note)
    {
        var (profile, evidence) = draft.Export(note, DateTimeOffset.UtcNow);
        profilePath = Path.GetFullPath(profilePath);
        if (File.Exists(profilePath)) throw new IOException("校准文件已存在；请使用新文件名。");
        var evidenceName = Path.GetFileNameWithoutExtension(profilePath) + "." + Guid.NewGuid().ToString("N") + ".evidence.json";
        var options = new JsonSerializerOptions(TelemetryJson.Options) { WriteIndented = true };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(evidence, options);
        var parent = Path.GetDirectoryName(profilePath)!;
        Directory.CreateDirectory(parent);
        await using (var file = new FileStream(Path.Combine(parent, evidenceName), FileMode.CreateNew, FileAccess.Write))
            await file.WriteAsync(bytes);
        // Evidence is fully written before publishing a usable profile; failure may leave an inert evidence file.
        profile = profile with { Evidence = evidenceName, EvidenceSha256 = Convert.ToHexString(SHA256.HashData(bytes)) };
        await using var output = new FileStream(profilePath, FileMode.CreateNew, FileAccess.Write);
        await JsonSerializer.SerializeAsync(output, profile, options);
    }
}
