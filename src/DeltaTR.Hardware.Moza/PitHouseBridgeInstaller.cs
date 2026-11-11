using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeltaTR.Hardware.Moza;

/// <summary>Version-pinned, reversible installation of the dedicated display field.</summary>
public static class PitHouseBridgeInstaller
{
    public const string Field = "v1/gameData/DeltaTRRadio";
    private static BridgeInstallation Store => new(BridgeSettings.Folder,
        "7A76BE6E4052404395D1B65BB052CB324D30D1012B5BC409F4F0C78092001B94",
        "A313A50C94C4EFAEB4D72C14F5CC9A4DB1C8265DA8E24D7EAA725E9DB1355927",
        "9AFA56EC4104E96949597F896EE69EEC61901007836E703A8D28B33430A9721C");
    public static bool IsInstalled => Store.IsInstalled;
    public static void Install(string executable, BridgeSettings settings) => Store.Install(executable, settings);
    public static void Restore() => Store.Restore();
    public static string FindExecutable()
    {
        foreach (var process in Process.GetProcessesByName("MOZA Pit House"))
        {
            using (process)
            {
                try
                {
                    string? path = process.MainModule?.FileName;
                    if (path is not null && Directory.Exists(Path.Combine(Path.GetDirectoryName(path)!, "CustomQmlPlugin"))) return path;
                }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        return "";
    }
}

// An isolated store permits recovery tests without touching the user's installation.
internal sealed class BridgeInstallation(string folder, string exeHash, string originalHash, string telemetryHash)
{
    private sealed record TelemetryFile(string Target, string Original, string Installed, string? PreviousInstalled = null);
    private sealed record Manifest(string Target, string Original, string Installed, string ConnectionHash,
        int FormatVersion = 1, TelemetryFile? Telemetry = null, string? PreviousInstalled = null, int BridgeRevision = 2);
    private string ManifestPath => Path.Combine(folder, "installation.json");
    private string BackupPath => Path.Combine(folder, "PageManager.original.qml");
    private string TelemetryBackup => Path.Combine(folder, "Telemetry.original.json");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private Manifest? ReadManifest() => File.Exists(ManifestPath)
        ? JsonSerializer.Deserialize<Manifest>(File.ReadAllText(ManifestPath)) : null;
    private static string Bin(Manifest manifest) => Path.GetDirectoryName(Path.GetDirectoryName(manifest.Target))!;
    private void ValidateManifest(Manifest m)
    {
        if (m.Original != originalHash || !Path.IsPathFullyQualified(m.Target) ||
            !string.Equals(m.Target, Path.Combine(Bin(m), "CustomQmlPlugin", "PageManager.qml"), StringComparison.OrdinalIgnoreCase) ||
            m.FormatVersion is < 1 or > 2 ||
            (m.FormatVersion == 2 && (m.Telemetry is null || m.Telemetry.Original != telemetryHash ||
                !string.Equals(m.Telemetry.Target, Path.Combine(Bin(m), "GameConfigs", "Telemetry.json"), StringComparison.OrdinalIgnoreCase))))
            throw new IOException("桥接安装记录校验失败，未修改文件。");
    }
    public bool IsInstalled
    {
        get
        {
            try
            {
                var m = ReadManifest();
                if (m is null) return false;
                ValidateManifest(m);
                return m.FormatVersion == 2 && m.BridgeRevision == 4 && Hash(File.ReadAllBytes(m.Target)) == m.Installed &&
                    Hash(File.ReadAllBytes(m.Telemetry!.Target)) == m.Telemetry.Installed &&
                    Hash(File.ReadAllBytes(Path.Combine(Bin(m), "MOZA Pit House.exe"))) == exeHash;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return false; }
        }
    }
    public void Install(string executable, BridgeSettings settings)
    {
        if (settings.Port is < 1024 or > 65535 || settings.Key.Length != 64 || !settings.Key.All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid bridge settings");
        executable = Path.GetFullPath(executable);
        if (Hash(File.ReadAllBytes(executable)) != exeHash)
            throw new InvalidOperationException("当前仅验证 Pit House 1.4.2.3 的指定版本，检测到版本不同，未修改文件。");
        string bin = Path.GetDirectoryName(executable)!;
        string target = Path.Combine(bin, "CustomQmlPlugin", "PageManager.qml");
        string telemetryTarget = Path.Combine(bin, "GameConfigs", "Telemetry.json");
        var existing = ReadManifest();
        if (existing is not null)
        {
            ValidateManifest(existing);
            if (!string.Equals(existing.Target, target, StringComparison.OrdinalIgnoreCase))
                throw new IOException("已有另一位置的桥接备份，请先恢复原文件。");
        }
        string connectionHash = Hash(Encoding.UTF8.GetBytes(settings.Port + ":" + settings.Key));
        byte[] currentQml = File.ReadAllBytes(target), currentTelemetry = File.ReadAllBytes(telemetryTarget);
        string qmlHash = Hash(currentQml), tableHash = Hash(currentTelemetry);
        if (qmlHash != originalHash && (existing is null || (qmlHash != existing.Installed && qmlHash != existing.PreviousInstalled)))
            throw new IOException("Pit House 界面文件已改变，未覆盖，请保留备份。");
        if (tableHash != telemetryHash && (existing?.FormatVersion != 2 ||
            tableHash != existing.Telemetry!.Installed && tableHash != existing.Telemetry.PreviousInstalled))
            throw new IOException("Pit House 遥测定义文件已改变，未覆盖，请保留备份。");
        byte[] original = qmlHash == originalHash ? currentQml : ReadBackup(BackupPath, originalHash);
        byte[] table = tableHash == telemetryHash ? currentTelemetry : ReadBackup(TelemetryBackup, telemetryHash);
        if (File.Exists(BackupPath)) ReadBackup(BackupPath, originalHash);
        if (File.Exists(TelemetryBackup)) ReadBackup(TelemetryBackup, telemetryHash);
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("DeltaTR.Hardware.Moza.PitHouseBridge.qml")!;
        using var reader = new StreamReader(resource);
        string bridge = reader.ReadToEnd().Replace("@@PORT@@", settings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("@@KEY@@", settings.Key);
        using var dashboardResource = Assembly.GetExecutingAssembly().GetManifestResourceStream("DeltaTR.Hardware.Moza.PitHouseDashboard.js")!;
        using var dashboardReader = new StreamReader(dashboardResource);
        bridge = bridge.Replace("@@DASHBOARD_SWITCH@@", dashboardReader.ReadToEnd());
        string source = Encoding.UTF8.GetString(original);
        int closing = source.LastIndexOf('}');
        if (closing < 0 || source.Contains("DELTATR_PITHOUSE_BRIDGE", StringComparison.Ordinal)) throw new IOException("无法识别原始界面结构。");
        byte[] candidate = Encoding.UTF8.GetBytes(source[..closing] + "\n// DELTATR_PITHOUSE_BRIDGE_V4\n" + bridge + "\n" + source[closing..]);
        var document = JsonNode.Parse(table)!.AsObject();
        var sectors = document["sectors"]!.AsArray();
        if (sectors.Any(x => (string?)x?["url"] == PitHouseBridgeInstaller.Field)) throw new IOException("专用字段已存在，未覆盖。");
        var entry = sectors.Single(x => (string?)x?["url"] == "v1/gameData/PlayerName")!.DeepClone();
        entry["name"] = "DeltaTRRadio"; entry["url"] = PitHouseBridgeInstaller.Field; entry["package_level"] = 500;
        entry["short_name"] = new JsonObject { ["zh_CN"] = "DeltaTR 无线电", ["en_US"] = "DeltaTR Radio" };
        entry["descs"] = new JsonObject { ["zh_CN"] = "DeltaTR 独立语音显示", ["en_US"] = "DeltaTR dedicated voice display" };
        for (int row = 0; row < WheelScreen.RowCount; row++)
        {
            var line = entry.DeepClone();
            line["url"] = MozaDisplaySession.Field(row);
            line["name"] = MozaDisplaySession.Field(row).Split('/')[^1];
            sectors.Add(line);
        }
        byte[] candidateTable = Encoding.UTF8.GetBytes(document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        if (existing?.FormatVersion == 2 && existing.BridgeRevision == 4 && qmlHash == Hash(candidate) && tableHash == Hash(candidateTable) &&
            existing.ConnectionHash == connectionHash) return;
        Directory.CreateDirectory(folder);
        SaveBackup(BackupPath, original, originalHash); SaveBackup(TelemetryBackup, table, telemetryHash);
        // Durable recovery metadata precedes BOTH mutations; partial installations can be restored.
        var manifest = new Manifest(target, originalHash, Hash(candidate), connectionHash, 2,
            new(telemetryTarget, telemetryHash, Hash(candidateTable), tableHash == telemetryHash ? null : tableHash),
            qmlHash == originalHash ? null : qmlHash, 4);
        Replace(ManifestPath, JsonSerializer.SerializeToUtf8Bytes(manifest));
        Replace(telemetryTarget, candidateTable);
        Replace(target, candidate);
    }
    public void Restore()
    {
        var m = ReadManifest() ?? throw new InvalidOperationException("没有安装桥接。");
        ValidateManifest(m);
        byte[] qml = ReadBackup(BackupPath, originalHash);
        string current = Hash(File.ReadAllBytes(m.Target));
        if (current != originalHash && current != m.Installed && current != m.PreviousInstalled)
            throw new IOException("Pit House 界面文件在安装后发生改变，未覆盖；请保留备份。");
        byte[]? table = null;
        if (m.FormatVersion == 2)
        {
            table = ReadBackup(TelemetryBackup, telemetryHash);
            string currentTable = Hash(File.ReadAllBytes(m.Telemetry!.Target));
            if (currentTable != telemetryHash && currentTable != m.Telemetry.Installed && currentTable != m.Telemetry.PreviousInstalled)
                throw new IOException("Pit House 遥测定义在安装后发生改变，未覆盖；请保留备份。");
        }
        // Validate every backup and target before touching either file; interrupted restores are retryable.
        Replace(m.Target, qml);
        if (table is not null) Replace(m.Telemetry!.Target, table);
    }
    private static byte[] ReadBackup(string path, string expected)
    {
        var bytes = File.ReadAllBytes(path);
        if (Hash(bytes) != expected) throw new IOException("原始备份校验失败，未覆盖。");
        return bytes;
    }
    private static void SaveBackup(string path, byte[] bytes, string expected)
    {
        if (File.Exists(path)) { ReadBackup(path, expected); return; }
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(bytes); output.Flush(true);
    }
    private static void Replace(string path, byte[] bytes)
    {
        string temporary = path + ".deltatr-" + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, bytes); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        if (Hash(File.ReadAllBytes(path)) != Hash(bytes)) throw new IOException("桥接文件写入校验失败，请恢复原文件后重试。");
    }
}
