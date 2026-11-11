using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DeltaTR.Hardware.Moza;

internal static class MozaInstallationTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static string Hash(byte[] value) => Convert.ToHexString(SHA256.HashData(value));
    private static void Reject(Action action)
    {
        try { action(); } catch (IOException) { return; }
        throw new Exception("Expected refusal without mutation");
    }
    public static void Recovery()
    {
        var root = Path.Combine(Path.GetTempPath(), "DeltaTR-bridge-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bin = Path.Combine(root, "bin"); var backup = Path.Combine(root, "backup");
            Directory.CreateDirectory(Path.Combine(bin, "CustomQmlPlugin")); Directory.CreateDirectory(Path.Combine(bin, "GameConfigs"));
            string executable = Path.Combine(bin, "MOZA Pit House.exe"), qml = Path.Combine(bin, "CustomQmlPlugin", "PageManager.qml"), table = Path.Combine(bin, "GameConfigs", "Telemetry.json");
            byte[] exeBytes = "fixture exe"u8.ToArray(), qmlBytes = "import QtQuick 2.0\nQtObject { }\n"u8.ToArray();
            byte[] tableBytes = Encoding.UTF8.GetBytes("""{"sectors":[{"name":"PlayerName","url":"v1/gameData/PlayerName","data_type":"string","compression":"string","default":"","package_level":500},{"name":"Rpm","url":"v1/gameData/Rpm","data_type":"float"}],"other":"retained"}""");
            File.WriteAllBytes(executable, exeBytes); File.WriteAllBytes(qml, qmlBytes); File.WriteAllBytes(table, tableBytes);
            var store = new BridgeInstallation(backup, Hash(exeBytes), Hash(qmlBytes), Hash(tableBytes));
            var settings = new BridgeSettings(17895, new string('A', 64));
            Check(!store.IsInstalled, "fresh is not installed");
            store.Install(executable, settings);
            Check(store.IsInstalled, "both files installed");
            var result = JsonNode.Parse(File.ReadAllBytes(table))!;
            var sectors = result["sectors"]!.AsArray(); var before = JsonNode.Parse(tableBytes)!;
            Check(sectors.Count == 7 && JsonNode.DeepEquals(sectors[0], before["sectors"]![0]) && JsonNode.DeepEquals(sectors[1], before["sectors"]![1]), "existing game fields preserved");
            Check((string?)sectors[2]!["url"] == PitHouseBridgeInstaller.Field && (string?)sectors[2]!["data_type"] == "string", "dedicated string registered");
            Check(!File.ReadAllText(qml).Contains("PlayerName"), "bridge never writes player name");
            byte[] installedQml = File.ReadAllBytes(qml), installedTable = File.ReadAllBytes(table);
            store.Install(executable, settings);
            Check(installedQml.SequenceEqual(File.ReadAllBytes(qml)) && installedTable.SequenceEqual(File.ReadAllBytes(table)), "idempotent installation");
            var v2 = JsonNode.Parse(File.ReadAllText(Path.Combine(backup, "installation.json")))!;
            v2["BridgeRevision"] = 2;
            File.WriteAllText(Path.Combine(backup, "installation.json"), v2.ToJsonString());
            Check(!store.IsInstalled, "old runtime revision must be upgraded");
            store.Install(executable, settings);
            Check(store.IsInstalled && File.ReadAllText(qml).Contains("createDeltaTrDashboardSwitch"), "V2 installation migrates to switching runtime");
            File.AppendAllText(table, " ");
            Check(!store.IsInstalled, "detect changed telemetry definition");
            Reject(store.Restore); Reject(() => store.Install(executable, settings));
            Check(installedQml.SequenceEqual(File.ReadAllBytes(qml)), "all targets validated before restore writes");
            File.WriteAllBytes(table, installedTable);
            File.AppendAllText(Path.Combine(backup, "Telemetry.original.json"), " ");
            Reject(store.Restore);
            Check(installedQml.SequenceEqual(File.ReadAllBytes(qml)), "corrupt backup leaves files untouched");
            File.WriteAllBytes(Path.Combine(backup, "Telemetry.original.json"), tableBytes);
            // Simulate a crash after the first of the two installation writes.
            File.WriteAllBytes(qml, qmlBytes);
            Check(!store.IsInstalled, "partial install is not enabled");
            store.Restore(); store.Restore();
            Check(File.ReadAllBytes(qml).SequenceEqual(qmlBytes) && File.ReadAllBytes(table).SequenceEqual(tableBytes), "partial install restores byte for byte");
            // Legacy manifest and existing V1 bridge migrate in-place without overwriting vanilla backups.
            byte[] legacy = Encoding.UTF8.GetBytes("legacy bridge fixture");
            File.WriteAllBytes(qml, legacy);
            File.WriteAllText(Path.Combine(backup, "installation.json"), JsonSerializer.Serialize(new { Target = qml, Original = Hash(qmlBytes), Installed = Hash(legacy), ConnectionHash = Hash(Encoding.UTF8.GetBytes(settings.Port + ":" + settings.Key)) }));
            Check(!store.IsInstalled, "legacy shared field cannot enable new server");
            store.Install(executable, settings);
            Check(store.IsInstalled && File.ReadAllBytes(Path.Combine(backup, "PageManager.original.qml")).SequenceEqual(qmlBytes), "legacy upgrade keeps original backup");
            File.WriteAllBytes(qml, legacy); // Crash during V1 -> V2 migration.
            store.Restore();
            Check(File.ReadAllBytes(qml).SequenceEqual(qmlBytes) && File.ReadAllBytes(table).SequenceEqual(tableBytes), "interrupted legacy migration restores both files");
            store.Install(executable, settings);
            File.WriteAllBytes(table, tableBytes); // Crash during a restore is also retryable.
            store.Restore();
            Check(!store.IsInstalled && File.ReadAllBytes(qml).SequenceEqual(qmlBytes), "interrupted restore finishes");
            File.WriteAllText(executable, "new vendor version");
            try { store.Install(executable, settings); throw new Exception("unsupported exe accepted"); }
            catch (InvalidOperationException) { }
            Check(File.ReadAllBytes(qml).SequenceEqual(qmlBytes), "version mismatch does not mutate");
        }
        finally { Directory.Delete(root, true); }
    }
}
