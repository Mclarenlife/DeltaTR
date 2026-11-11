using System.Windows.Input;
using DeltaTR.App.Settings;
using DeltaTR.Engineer;
using DeltaTR.App.Input;
using DeltaTR.Voice;

namespace DeltaTR.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly string settingsPath;
    private readonly System.Net.Http.HttpClient? chatClient;
    private RadioSettings activeSettings = new();
    public TalkBinding SelectedTalkKey { get; private set; } = TalkBinding.Keyboard(0x77);
    public bool BindingInput { get; private set; }
    internal int BindingRevision { get; private set; }
    public string BindButtonLabel => BindingInput ? "取消绑定" : "重新绑定";
    public string BoundDeviceLabel => SelectedTalkKey.ToString();
    public string TalkHint => UseSmartWheelButton ? $"{SelectedTalkKey} · 短按切换画面，长按 0.5 秒讲话，松开发送" : $"按住 {SelectedTalkKey} 讲话，松开发送";
    public string BindingHelp { get; private set; } = "";
    internal void SetBindingHelp(string message)
    {
        if (BindingHelp == message) return;
        BindingHelp = message;
        Changed(nameof(BindingHelp));
    }
    internal void SetBindingMode(bool listening)
    {
        if (BindingInput == listening) return;
        if (listening) CancelVoice();
        BindingInput = listening;
        BindingRevision++;
        SetBindingHelp(listening ? "按一下键盘键或方向盘按钮，Esc 取消。" : "");
        Changed(nameof(BindingInput)); Changed(nameof(BindButtonLabel));
    }
    internal void AcceptBinding(TalkBinding binding)
    {
        if (!BindingInput || !binding.IsValid) return;
        SetBindingMode(false);
        var next = activeSettings with { PushToTalkBinding = binding };
        try { next.Save(settingsPath); }
        catch (Exception e) { SetBindingHelp("讲话键保存失败：" + e.Message); return; }
        activeSettings = next;
        SelectedTalkKey = binding;
        Changed(nameof(SelectedTalkKey)); Changed(nameof(BoundDeviceLabel));
        Changed(nameof(TalkLabel)); Changed(nameof(TalkHint));
        SetBindingHelp("绑定已保存。按住讲话，松开发送。");
    }
    public bool MetricUnits { get => !ImperialUnits; set { if (value) SetDisplayUnits(false); } }
    public bool ImperialUnits { get => activeSettings.ImperialUnits; set { if (value) SetDisplayUnits(true); } }

    private void SetDisplayUnits(bool imperial)
    {
        if (imperial == ImperialUnits) return;
        var next = activeSettings with { ImperialUnits = imperial };
        try { next.Save(settingsPath); }
        catch (Exception e)
        {
            Notice = "显示单位保存失败：" + e.Message;
            Changed(nameof(ImperialUnits)); Changed(nameof(MetricUnits));
            return;
        }
        activeSettings = next;
        Changed(nameof(ImperialUnits)); Changed(nameof(MetricUnits));
        CancelVoice();
        Refresh();
        if (liveQuestion is null) Answer = "单位已切换，可以继续提问。";
    }

    private string activeKey = "";
    private AudioDevice? selectedInput, selectedOutput;
    private string settingsStatus = "设备与阈值即时生效，点击保存可供下次使用。";
    private string lastInput = "按住讲话键说一句话，松开查看输入与识别结果。";
    private int inputThreshold = 2, confidence = 35;
    public List<AudioDevice> InputDevices { get; private set; } = [];
    public List<AudioDevice> OutputDevices { get; private set; } = [];
    public AudioDevice? SelectedInput
    {
        get => selectedInput;
        set { if (selectedInput == value) return; StopMicrophoneTest(); selectedInput = value; Changed(); }
    }
    public AudioDevice? SelectedOutput
    {
        get => selectedOutput;
        set { selectedOutput = value; speech.OutputDeviceNumber = value?.Number ?? -1; Changed(); }
    }
    public bool UseWindowsRecognition { get => activeSettings.EffectiveEngine == SpeechEngine.Windows; set { if (value) SetEngine(SpeechEngine.Windows); } }
    public bool UseZipformer { get => activeSettings.EffectiveEngine == SpeechEngine.Zipformer; set { if (value) SetEngine(SpeechEngine.Zipformer); } }
    public bool UseSenseVoice { get => activeSettings.EffectiveEngine == SpeechEngine.SenseVoice; set { if (value) SetEngine(SpeechEngine.SenseVoice); } }
    private void SetEngine(SpeechEngine engine)
    {
        if (engine == activeSettings.EffectiveEngine) return;
        try
        {
            var next = activeSettings with { RecognitionEngine = engine, UseZipformer = engine == SpeechEngine.Zipformer };
            next.Save(settingsPath);
            activeSettings = next;
            CancelVoice();
            if (engine != SpeechEngine.Zipformer) _ = zipformer.UnloadAsync();
            if (engine != SpeechEngine.SenseVoice) _ = senseVoice.UnloadAsync();
            SettingsStatus = "识别引擎已切换并保存。";
        }
        catch (Exception e) { SettingsStatus = "无法保存识别引擎：" + e.Message; }
        Changed(nameof(UseWindowsRecognition)); Changed(nameof(UseZipformer)); Changed(nameof(UseSenseVoice));
        Changed(nameof(RecognitionDescription)); Changed(nameof(CanTalk));
    }
    private bool enhanceVoice, reduceNoise;
    public bool EnhanceVoice
    {
        get => enhanceVoice;
        set { if (enhanceVoice == value) return; enhanceVoice = value; Changed(); ApplyInputProcessing(); }
    }
    public bool ReduceNoise
    {
        get => reduceNoise;
        set { if (reduceNoise == value) return; reduceNoise = value; Changed(); ApplyInputProcessing(); }
    }
    private void ApplyInputProcessing()
    {
        levelMonitor?.SetProcessing(EnhanceVoice, ReduceNoise);
        microphone?.SetProcessing(EnhanceVoice, ReduceNoise);
    }

    public int InputThreshold { get => inputThreshold; set { inputThreshold = Math.Clamp(value, 0, 100); Changed(); RefreshInputMeter(); } }
    public int Confidence { get => confidence; set { confidence = Math.Clamp(value, 1, 100); Changed(); } }
    public string WakeWordEdit { get; set; } = "工程师";
    public string ApiAddress { get; set; } = "";
    public string ApiModel { get; set; } = "";
    public string ApiKeyEdit { get; set; } = "";
    public string WakeHint => $"说“{activeSettings.WakeWord}，我应该第几圈进站”咨询 AI。每次 AI 提问都以唤醒词开头。";
    public string SettingsStatus { get => settingsStatus; private set { settingsStatus = value; Changed(); } }
    public string LastInput { get => lastInput; private set { lastInput = value; Changed(); } }
    public ICommand SaveSettingsCommand { get; private set; } = null!;
    public ICommand RefreshDevicesCommand { get; private set; } = null!;

    private void InitializeSettings()
    {
        try
        {
            activeSettings = RadioSettings.Load(settingsPath);
            ApiKeyEdit = activeKey = activeSettings.Decrypt();
        }
        catch { SettingsStatus = "读取本机设置或解密密钥失败，请重新填写并保存。"; }
        var saved = activeSettings.PushToTalkBinding ?? TalkBinding.Keyboard(activeSettings.TalkKey);
        if (saved.IsValid) SelectedTalkKey = saved;
        ApiAddress = activeSettings.Address;
        ApiModel = activeSettings.Model;
        WakeWordEdit = activeSettings.WakeWord;
        EnhanceVoice = activeSettings.EnhanceVoice;
        ReduceNoise = activeSettings.ReduceNoise;
        InputThreshold = activeSettings.InputThreshold;
        Confidence = activeSettings.Confidence;
        Volume = activeSettings.Volume;
        LoadOverlayStyle();
        RefreshDevices(activeSettings.InputName, activeSettings.OutputName);
        SaveSettingsCommand = GameCommand(() =>
        {
            var word = WakeWordEdit.Trim();
            if (word.Length is < 1 or > 24 || word.Any(c => !char.IsLetterOrDigit(c)))
                throw new ArgumentException("唤醒词请填写 1 至 24 个汉字、字母或数字。");
            var address = ApiAddress.Trim();
            var model = ApiModel.Trim();
            if (address.Length != 0 || model.Length != 0 || ApiKeyEdit.Length != 0)
            {
                AiRadio.Endpoint(address);
                if (model.Length == 0) throw new ArgumentException("请填写 API 模型名称。");
            }
            var key = ApiKeyEdit.Trim();
            var nextQueries = new TelemetryQueries(CurrentPhrases, word, SelectedGame.Id ?? Core.RacingGame.ACC, currentCatalog);
            var next = WithOverlayStyle(activeSettings) with { InputName = SelectedInput?.Name ?? "", OutputName = SelectedOutput?.Name ?? "",
                EnhanceVoice = EnhanceVoice, ReduceNoise = ReduceNoise,
                InputThreshold = InputThreshold, Confidence = Confidence, Volume = Volume, WakeWord = word,
                Address = address, Model = model, ProtectedKey = RadioSettings.Encrypt(key) };
            next.Save(settingsPath);
            CancelVoice();
            activeSettings = next;
            activeKey = key;
            queries = nextQueries;
            Changed(nameof(PhraseEditor));
            Changed(nameof(WakeHint));
            SettingsStatus = "已保存，新的设置已生效。";
            return Task.CompletedTask;
        }, e => SettingsStatus = "保存失败：" + e.Message);
        RefreshDevicesCommand = GameCommand(() =>
        {
            RefreshDevices(SelectedInput?.Name, SelectedOutput?.Name);
            return Task.CompletedTask;
        }, e => SettingsStatus = e.Message);
    }

    private void RefreshDevices(string? inputName, string? outputName)
    {
        try
        {
            InputDevices = AudioDevice.List(true);
            OutputDevices = AudioDevice.List(false);
        }
        catch (Exception e)
        {
            SettingsStatus = "枚举设备失败，请重新连接后刷新：" + e.Message;
            if (InputDevices.Count == 0) InputDevices = [new(-1, "跟随系统默认设备")];
            if (OutputDevices.Count == 0) OutputDevices = [new(-1, "跟随系统默认设备")];
        }
        Changed(nameof(InputDevices)); Changed(nameof(OutputDevices));
        SelectedInput = InputDevices.FirstOrDefault(d => d.Name == inputName) ?? InputDevices[0];
        SelectedOutput = OutputDevices.FirstOrDefault(d => d.Name == outputName) ?? OutputDevices[0];
        if ((!string.IsNullOrEmpty(inputName) && SelectedInput.Name != inputName) ||
            (!string.IsNullOrEmpty(outputName) && SelectedOutput.Name != outputName))
            SettingsStatus = "上次选择的设备未连接，已切换到系统默认设备。";
    }
}
