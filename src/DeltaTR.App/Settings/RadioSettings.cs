using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DeltaTR.App.Settings;

internal sealed record RadioSettings
{
    public string InputName { get; init; } = "";
    public string OutputName { get; init; } = "";
    public bool UseZipformer { get; init; } // Legacy setting retained for migration.
    public Voice.SpeechEngine? RecognitionEngine { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public Voice.SpeechEngine EffectiveEngine => RecognitionEngine ?? (UseZipformer ? Voice.SpeechEngine.Zipformer : Voice.SpeechEngine.Windows);
    public bool EnhanceVoice { get; init; }
    public bool ReduceNoise { get; init; }
    public int InputThreshold { get; init; } = 2;
    public int Confidence { get; init; } = 35;
    public bool ImperialUnits { get; init; }
    public Dictionary<string, Engineer.RaceAlertKind> RaceAlerts { get; init; } = [];
    public List<Engineer.AutomationRule> Automations { get; init; } = [];
    public Dictionary<string, Engineer.StrategyProfile> Strategies { get; init; } = [];
    public int Volume { get; init; } = 80;
    public Input.TalkBinding? PushToTalkBinding { get; init; }
    public int TalkKey { get; init; } = 0x77;
    public bool WheelSmartSwitch { get; init; } = true;
    public bool WheelAutoPopup { get; init; } = true;
    public int AvatarSizePercent { get; init; } = 100;
    public int BubbleSizePercent { get; init; } = 100;
    public int OverlayOpacityPercent { get; init; } = 100;
    public Dictionary<string, Dictionary<string, string[]>> GamePhrases { get; init; } = [];
    public Dictionary<string, string[]> PhraseOverrides { get; init; } = [];
    public int? TextOpacity { get; init; }
    public int? AvatarOpacity { get; init; }
    public int? BackdropOpacity { get; init; }
    public string TextPlacement { get; init; } = "Right";
    public string WakeWord { get; init; } = "工程师";
    public string Address { get; init; } = "";
    public string Model { get; init; } = "";
    public string ProtectedKey { get; init; } = "";

    internal static string FileName => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeltaTR", "radio.json");
    internal static RadioSettings Load(string path) => File.Exists(path)
        ? JsonSerializer.Deserialize<RadioSettings>(File.ReadAllText(path)) ?? new() : new() { RecognitionEngine = Voice.SpeechEngine.SenseVoice };
    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(this)); File.Move(temp, path, true); }
        finally { File.Delete(temp); }
    }

    internal static string Encrypt(string key) => key.Length == 0 ? "" : Convert.ToBase64String(
        ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
    internal string Decrypt() => ProtectedKey.Length == 0 ? "" : Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(ProtectedKey), null, DataProtectionScope.CurrentUser));
}
