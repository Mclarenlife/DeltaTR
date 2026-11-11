using System.Windows.Input;
using DeltaTR.App.Settings;
using DeltaTR.Voice;

namespace DeltaTR.App.ViewModels;

public sealed record TencentVoiceChoice(int Id, string Name);

public sealed partial class MainViewModel
{
    public IReadOnlyList<TencentVoiceChoice> TencentVoices { get; } =
        [new(403002, "云小帅 · 解说男声（大模型）"), new(403001, "云小和 · 聊天女声（大模型）"),
         new(101004, "智云 · 通用男声"), new(101018, "智靖 · 情感男声"), new(101001, "智瑜 · 情感女声")];
    private bool useTencentTts;
    private int speechRate;
    public int SpeechRate
    {
        get => speechRate;
        set { speechRate = Math.Clamp(value, -2, 2); Changed(); Changed(nameof(SpeechRateLabel)); }
    }
    public string SpeechRateLabel => SpeechRate switch { -2 => "慢速", -1 => "稍慢", 1 => "稍快", 2 => "快速", _ => "标准" };
    public bool UseTencentTts { get => useTencentTts; set { useTencentTts = value; Changed(); } }
    public string TencentAppId { get; set; } = "";
    public string TencentSecretId { get; set; } = "";
    public string TencentSecretKey { get; set; } = "";
    public TencentVoiceChoice? SelectedTencentVoice { get; set; }
    public ICommand SaveSpeechOutputCommand { get; private set; } = null!;
    public string SpeechOutputStatus { get; private set; } = "";
    private void OutputStatus(string message) { SpeechOutputStatus = message; Changed(nameof(SpeechOutputStatus)); }

    private TencentVoiceOptions? DraftSpeechOutput()
    {
        if (!UseTencentTts) return null;
        var options = new TencentVoiceOptions(TencentAppId, TencentSecretId, TencentSecretKey, SelectedTencentVoice?.Id ?? 0);
        options.Validate();
        return options;
    }

    private void InitializeSpeechOutput()
    {
        SpeechRate = activeSettings.SpeechRate;
        speech.Rate = SpeechRate;
        TencentAppId = activeSettings.TencentAppId;
        UseTencentTts = activeSettings.UseTencentTts;
        SelectedTencentVoice = TencentVoices.FirstOrDefault(v => v.Id == activeSettings.TencentVoiceId) ?? TencentVoices[0];
        try
        {
            TencentSecretId = RadioSettings.Decrypt(activeSettings.ProtectedTencentId);
            TencentSecretKey = RadioSettings.Decrypt(activeSettings.ProtectedTencentKey);
            speech.CloudVoice = DraftSpeechOutput();
        }
        catch { OutputStatus("腾讯云配置未能加载，请重新填写并保存。当前使用系统声音。"); }
        SaveSpeechOutputCommand = GameCommand(() =>
        {
            var options = DraftSpeechOutput();
            var next = activeSettings with
            {
                SpeechRate = SpeechRate,
                UseTencentTts = UseTencentTts, TencentAppId = TencentAppId.Trim(),
                TencentVoiceId = SelectedTencentVoice?.Id ?? TencentVoices[0].Id,
                ProtectedTencentId = RadioSettings.Encrypt(TencentSecretId.Trim()),
                ProtectedTencentKey = RadioSettings.Encrypt(TencentSecretKey.Trim())
            };
            next.Save(settingsPath);
            speech.StopSpeaking();
            activeSettings = next;
            speech.Rate = SpeechRate;
            speech.CloudVoice = options;
            Changed(nameof(OutputDescription));
            OutputStatus(options is null ? "已保存，使用 Windows 系统声音。" : "已保存，使用腾讯云 · " + SelectedTencentVoice!.Name);
            return Task.CompletedTask;
        }, e => OutputStatus("保存失败：" + e.Message));
    }
}
