using System.Windows.Input;
using System.Windows.Threading;
using DeltaTR.Voice;
using DeltaTR.Engineer;

namespace DeltaTR.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly WindowsSpeech speech = new();
    private readonly ZipformerSpeech zipformer = new();
    private readonly SenseVoiceSpeech senseVoice = new();
    private ZipformerTurn? zipformerTurn;
    private bool CanRecognize => UseSenseVoice ? senseVoice.Installed : UseZipformer ? zipformer.Installed : speech.CanRecognize;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer voiceTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? aiStop;
    private readonly List<RadioMessage> conversation = [];
    private string? conversationSession;
    private string? liveQuestion;
    private string? liveQuestionSession;
    // Tentative display text only. It never enters the query/AI path before final recognition.
    private string wheelInputText = "";
    private string wheelInputError = "";

    private MicrophoneCapture? microphone;
    private MicrophoneCapture? levelMonitor;
    public ICommand TestMicrophoneCommand { get; private set; } = null!;
    public string TestMicrophoneLabel => levelMonitor is null ? "测试麦克风" : "停止测试";
    public bool CanTestMicrophone => HasGame && !closing && !processing && !IsListening;
    private CancellationTokenSource? recognitionStop;
    private bool closing, processing, voiceEnabled = true, speakReplies = true;
    private int generation;
    private double audioLevel;
    private DateTimeOffset captureStarted;
    private string lastQuestion = "等待你的呼叫", voiceStatus = "待命";

    public ICommand SilenceCommand { get; private set; } = null!;
    public ICommand TestSpeakerCommand { get; private set; } = null!;
    public string LastQuestion { get => lastQuestion; private set { lastQuestion = value; Changed(); Changed(nameof(OverlayStatus)); } }
    public string VoiceStatus { get => voiceStatus; private set { voiceStatus = value; Changed(); Changed(nameof(BubbleText)); } }
    public bool IsListening => microphone is not null;
    internal int CaptureVersion => generation;
    public string TalkLabel => IsListening ? "正在聆听 · 松开发送" : processing ? "正在识别…" : $"按住讲话  ·  {SelectedTalkKey.Name}";
    public bool CanTalk => HasGame && voiceEnabled && CanRecognize && !processing && !closing;
    public double AudioLevel
    {
        get => audioLevel;
        internal set { audioLevel = value; Changed(); RefreshInputMeter(); }
    }
    public double AcceptedInputLevel => MicrophoneCapture.MeetsInputThreshold(AudioLevel, InputThreshold) ? AudioLevel : 0;
    public string InputMeterStatus => levelMonitor is null && !IsListening
        ? "点击测试麦克风查看输入，竖线表示阈值"
        : AcceptedInputLevel > 0 ? "当前输入已达阈值" : AudioLevel > 0 ? "当前输入低于阈值" : "等待声音输入";
    private void RefreshInputMeter()
    {
        Changed(nameof(AcceptedInputLevel));
        Changed(nameof(InputMeterStatus));
    }
    public bool VoiceEnabled { get => voiceEnabled; set { voiceEnabled = value; Changed(); Changed(nameof(CanTalk)); if (!value) CancelVoice(); } }
    public bool SpeakReplies { get => speakReplies; set { speakReplies = value; Changed(); if (!value) speech.StopSpeaking(); } }
    public int Volume { get; set; } = 80;
    public string RecognitionDescription => UseSenseVoice
        ? senseVoice.Installed ? "SenseVoice · 本机中文整句识别 · 支持词库匹配和唤醒词 · 首次加载稍慢" : "缺少 SenseVoice 模型，请使用完整发布目录。"
        : UseZipformer
        ? zipformer.Installed ? "Zipformer · 本机中英双语识别 · 词库和唤醒词已启用" : "缺少 Zipformer 模型，请使用完整的发布目录。"
        : speech.RecognitionDescription;
    public string OutputDescription => speech.OutputDescription;

    private string radioAnswerLabel = "比赛工程师";
    public string RadioAnswerLabel
    {
        get => radioAnswerLabel;
        private set { if (radioAnswerLabel != value) { radioAnswerLabel = value; Changed(); } }
    }

    private void RefreshLocalAnswer(Core.RaceSnapshot? snapshot, DateTimeOffset now)
    {
        if (liveQuestion is null)
        {
            RadioAnswerLabel = "比赛工程师";
            return;
        }
        if (snapshot?.SessionId != liveQuestionSession)
        {
            liveQuestion = null;
            RadioAnswerLabel = "比赛工程师";
            Answer = "驾驶会话已变化，请重新提问。";
            return;
        }
        var reply = queries.Answer(liveQuestion, snapshot, now);
        RadioAnswerLabel = reply.HasVerifiedData && snapshot is not null && reply.EvidenceFields.Length > 0
            ? $"遥测更新于 {reply.EvidenceFields.Min(key => snapshot.Reading(key)!.ObservedAt).ToLocalTime():HH:mm:ss}"
            : "比赛工程师";
        if (Answer != reply.Text) Answer = reply.Text;
    }

    private void InitializeVoice()
    {
        TestMicrophoneCommand = GameCommand(() =>
        {
            if (levelMonitor is not null) StopMicrophoneTest();
            else if (CanTestMicrophone) StartMicrophoneTest();
            return Task.CompletedTask;
        }, error =>
        {
            StopMicrophoneTest();
            LastInput = "麦克风测试失败：" + error.Message;
        });
        SilenceCommand = GameCommand(() => { raceAlerts.ClearPending(); automation.ClearPending(); speech.StopSpeaking(); VoiceStatus = "播报已停止"; return Task.CompletedTask; }, e => VoiceStatus = e.Message);
        TestSpeakerCommand = GameCommand(() => { if (!IsListening && !processing) speech.Speak("无线电检查，收到。", Volume); return Task.CompletedTask; }, e => VoiceStatus = e.Message);
        speech.PlaybackStatus += text => dispatcher.BeginInvoke(() => { if (!closing && !IsListening && !processing) VoiceStatus = text; });
        if (!CanRecognize) VoiceStatus = RecognitionDescription;
        voiceTimer.Tick += async (_, _) => { if (IsListening && DateTimeOffset.UtcNow - captureStarted > TimeSpan.FromSeconds(15)) await EndTalkAsync(); };
        voiceTimer.Start();
    }

    public void BeginTalk()
    {
        if (!CanTalk || IsListening) return;
        StopMicrophoneTest();
        SetBubbleTurn(BubbleTurn.Listening);
        generation++;
        wheelInputText = ""; wheelInputError = "";
        aiStop?.Cancel();
        speech.StopSpeaking();
        try
        {
            var capture = new MicrophoneCapture(SelectedInput?.Number ?? -1); microphone = capture;
            capture.SetProcessing(EnhanceVoice, ReduceNoise);
            capture.LevelChanged += level => dispatcher.BeginInvoke(() => { if (microphone == capture) AudioLevel = level; });
            if (UseZipformer || IsAnyWheelScreenRunning && zipformer.Installed)
            {
                var captureGeneration = generation;
                zipformerTurn = zipformer.Begin(activeSettings.WakeWord, CurrentPhrases.Values.SelectMany(x => x), queries.RecognitionPhrases,
                    text => dispatcher.BeginInvoke(() =>
                    {
                        if (microphone != capture || captureGeneration != generation || closing) return;
                        wheelInputText = text; UpdateWheelScreen();
                    }));
                capture.SamplesAvailable += zipformerTurn.Accept;
            }
            capture.Start(); captureStarted = DateTimeOffset.UtcNow;
            VoiceStatus = "正在聆听 · 最长 15 秒";
        }
        catch (Exception e) { zipformerTurn?.Dispose(); zipformerTurn = null; microphone?.Dispose(); microphone = null; VoiceStatus = "无法开始语音输入：" + e.Message; wheelInputError = VoiceStatus; hideAfter = DateTimeOffset.UtcNow + ReadingTime; }
        VoiceChanged();
    }

    public async Task EndTalkAsync()
    {
        var capture = microphone;
        if (capture is null) return;
        var engine = activeSettings.EffectiveEngine;
        var wakeWord = activeSettings.WakeWord;
        var vocabulary = queries.RecognitionPhrases.ToArray();
        using var zipTurn = zipformerTurn;
        zipformerTurn = null;
        // For offline/Windows engines the streaming turn provides a UI draft only.
        if (engine != SpeechEngine.Zipformer) zipTurn?.Dispose();
        microphone = null; processing = true; AudioLevel = 0; VoiceStatus = "正在识别…"; VoiceChanged();
        var turn = ++generation;
        using var cancellation = new CancellationTokenSource(); recognitionStop = cancellation;
        try
        {
            byte[] wave;
            using (capture)
            {
                wave = await capture.StopAsync();
                cancellation.Token.ThrowIfCancellationRequested();
                LastInput = $"峰值 {capture.PeakPercent:0.0}% · 时长 {capture.CapturedMilliseconds / 1000d:0.0} 秒";
                if (capture.CapturedMilliseconds < 120 || !MicrophoneCapture.MeetsInputThreshold(capture.PeakPercent, InputThreshold))
                {
                    VoiceStatus = capture.CapturedMilliseconds < 120 ? "讲话时间太短，请按住后说完整问题。"
                        : $"输入峰值 {capture.PeakPercent:0.0}%（阈值 {InputThreshold}%），请检查麦克风或降低阈值。";
                    LastInput += " · " + VoiceStatus;
                    wheelInputError = VoiceStatus;
                    return;
                }
            }
            var result = engine == SpeechEngine.SenseVoice
                ? await senseVoice.RecognizeAsync(wave, wakeWord, vocabulary, cancellation.Token)
                : engine == SpeechEngine.Zipformer && zipTurn is not null ? await zipTurn.FinishAsync(cancellation.Token)
                : await speech.RecognizeAsync(wave, cancellation.Token, wakeWord, Confidence / 100f, vocabulary);
            if (closing || turn != generation) return;
            LastInput += " · " + result.Message;
            if (result.Text is not null && result.Confidence is { } confidence) LastInput += $"（置信度 {confidence:P0}）";
            if (result.Text is null) { VoiceStatus = result.Message; wheelInputError = result.Message; }
            else
            {
                wheelInputText = result.Text;
                UpdateWheelScreen();
                if (IsAnyWheelScreenRunning) await Task.Delay(650, cancellation.Token);
                if (closing || turn != generation) return;
                processing = false; VoiceChanged();
                Question = result.Text; await SubmitQuestionAsync(result.Text);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { if (!closing && turn == generation) { VoiceStatus = "语音失败，请检查设备后重试：" + e.Message; wheelInputError = VoiceStatus; } }
        finally
        {
            if (recognitionStop == cancellation)
            {
                if (bubbleTurn == BubbleTurn.Listening) hideAfter = DateTimeOffset.UtcNow + ReadingTime;
                recognitionStop = null;
                processing = false;
                VoiceChanged();
            }
        }
    }

    public void CancelVoice()
    {
        StopMicrophoneTest();
        SetBubbleTurn(BubbleTurn.Idle);
        generation++; recognitionStop?.Cancel();
        wheelInputText = ""; wheelInputError = "";
        aiStop?.Cancel(); conversation.Clear(); conversationSession = null;
        var capture = microphone; microphone = null; capture?.Dispose();
        zipformerTurn?.Dispose(); zipformerTurn = null;
        speech.StopSpeaking(); AudioLevel = 0; VoiceStatus = !CanRecognize ? RecognitionDescription : VoiceEnabled ? "待命 · 麦克风已关闭" : "按键讲话已关闭"; VoiceChanged();
    }

    private void VoiceChanged() { Changed(nameof(InputMeterStatus)); Changed(nameof(IsListening)); Changed(nameof(TalkLabel)); Changed(nameof(CanTalk)); Changed(nameof(CanTestMicrophone)); }

    private void StartMicrophoneTest()
    {
        var monitor = new MicrophoneCapture(SelectedInput?.Number ?? -1, levelOnly: true);
        levelMonitor = monitor;
        monitor.SetProcessing(EnhanceVoice, ReduceNoise);
        monitor.LevelChanged += level => dispatcher.BeginInvoke(() =>
        {
            if (levelMonitor == monitor) AudioLevel = level;
        });
        monitor.MonitorEnded += error => dispatcher.BeginInvoke(() =>
        {
            if (levelMonitor != monitor) return;
            StopMicrophoneTest();
            if (error is not null) LastInput = "麦克风连接中断：" + error.Message;
        });
        monitor.Start();
        Changed(nameof(InputMeterStatus));
        LastInput = $"正在测试「{SelectedInput?.Name}」，请讲话并观察电平。";
        VoiceStatus = "正在测试麦克风";
        Changed(nameof(TestMicrophoneLabel));
    }

    private void StopMicrophoneTest()
    {
        if (levelMonitor is not { } monitor) return;
        levelMonitor = null;
        monitor.Dispose();
        AudioLevel = 0;
        LastInput = $"麦克风测试已结束 · 峰值 {monitor.PeakPercent:0.0}%";
        if (VoiceStatus == "正在测试麦克风") VoiceStatus = "待命 · 麦克风已关闭";
        Changed(nameof(TestMicrophoneLabel));
    }
    internal async Task SubmitQuestionAsync(string text)
    {
        if (!HasGame || string.IsNullOrWhiteSpace(text) || closing) return;
        aiStop?.Cancel();
        var turn = ++generation;
        wheelInputError = "";
        speech.StopSpeaking();
        SetBubbleTurn(BubbleTurn.Reply);
        LastQuestion = text.Trim();
        var native = stopped ? null : store.Latest;
        strategyTelemetry.Observe(native, DateTimeOffset.UtcNow);
        var snapshot = Core.DisplayUnits.ForDisplay(native, ImperialUnits);
        liveQuestion = null;
        RadioAnswerLabel = "比赛工程师";
        var questionForAi = AiRadio.AfterWakeWord(text, activeSettings.WakeWord);
        if (questionForAi is null)
        {
            liveQuestion = text;
            liveQuestionSession = snapshot?.SessionId;
            RefreshLocalAnswer(snapshot, DateTimeOffset.UtcNow);
            VoiceStatus = "遥测回答已更新";
        }
        else if (questionForAi.Length == 0)
        {
            Answer = $"收到，请说“{activeSettings.WakeWord}”并接上你的问题。";
            VoiceStatus = "等待 AI 问题";
        }
        else if (activeSettings.Address.Length == 0 || activeSettings.Model.Length == 0)
        {
            Answer = "已收到 AI 呼叫，请先在设置中填写 API 地址、模型并保存。";
            VoiceStatus = "AI 尚未配置";
        }
        else
        {
            using var cancellation = new CancellationTokenSource();
            aiStop = cancellation;
            if (conversationSession != snapshot?.SessionId) conversation.Clear();
            conversationSession = snapshot?.SessionId;
            VoiceStatus = "AI 正在回答…";
            Answer = "正在等待工程师回复…";
            try
            {
                var reply = await AiRadio.AskAsync(AiRadio.Endpoint(activeSettings.Address), activeSettings.Model, activeKey,
                    questionForAi, snapshot, conversation, cancellation.Token, chatClient, CurrentStrategy, strategyTelemetry.Laps.ToArray());
                if (closing || turn != generation || cancellation.IsCancellationRequested) return;
                if (snapshot?.SessionId != store.Latest?.SessionId || snapshot?.Phase != store.Latest?.Phase)
                {
                    Answer = "驾驶会话已变化，请重新提问。";
                    VoiceStatus = "会话已变化";
                    return;
                }
                conversation.Add(new("user", questionForAi));
                conversation.Add(new("assistant", reply));
                if (conversation.Count > 6) conversation.RemoveRange(0, 2);
                Answer = (isDemo ? "【演示数据】" : "") + reply;
                VoiceStatus = "AI 已回复";
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
            catch (Exception e)
            {
                if (turn != generation || closing) return;
                Answer = e is OperationCanceledException ? "AI 请求超时，请重试。" : "AI 请求失败：" + e.Message;
                VoiceStatus = "AI 未能回答";
            }
            finally { if (aiStop == cancellation) aiStop = null; }
        }
        hideAfter = DateTimeOffset.UtcNow + (IsAnyWheelScreenRunning
            ? DeltaTR.Hardware.Moza.WheelScreen.ReadingDuration(Answer) : ReadingTime);
        if (SpeakReplies) speech.Speak(Answer, Volume);
    }
}
