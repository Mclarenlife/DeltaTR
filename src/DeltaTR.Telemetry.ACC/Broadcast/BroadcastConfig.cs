using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeltaTR.Telemetry.ACC.Broadcast;

public static class BroadcastConfig
{
    private static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Assetto Corsa Competizione", "Config", "broadcasting.json");

    internal static (int Port, string Password) Read(string? path = null)
    {
        var (json, _) = Load(path ?? ConfigPath);
        return (json["updListenerPort"]?.GetValue<int>() ?? 0, json["connectionPassword"]?.GetValue<string>() ?? "");
    }

    // Only invoked by the settings button. Preserve the other keys and ACC's UTF-16 encoding.
    public static string Enable() => Enable(ConfigPath);
    internal static string Enable(string path)
    {
        var (json, encoding) = Load(path);
        if (json["updListenerPort"]?.GetValue<int>() is > 0 and <= 65535)
            return "ACC 广播端口已开启，可点击重新检测。";
        json["updListenerPort"] = 9000;
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), encoding);
            File.Replace(temp, path, null);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        return "已启用 ACC 广播端口 9000，请在方便时重启 ACC。";
    }

    private static (JsonObject Json, Encoding Encoding) Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        Encoding encoding = bytes.Length >= 2 && (bytes[1] == 0 || bytes[0] == 0xff)
            ? new UnicodeEncoding(false, bytes[0] == 0xff)
            : new UTF8Encoding(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
        var text = encoding.GetString(bytes).Trim('\uFEFF', '\0');
        return (JsonNode.Parse(text)?.AsObject() ?? throw new InvalidDataException("ACC 广播配置为空"), encoding);
    }
}
