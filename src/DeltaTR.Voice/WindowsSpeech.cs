using System.Speech.Recognition;
using System.Speech.Synthesis;
using DeltaTR.Engineer;
using NAudio.Wave;

namespace DeltaTR.Voice;

public sealed record VoiceResult(string? Text, string Message, float? Confidence = null, string? Candidate = null);

/// <summary>Chinese offline SAPI speech. No microphone is opened by this class.</summary>
public sealed class WindowsSpeech : IDisposable
{
    private string? speakerName;
    private RecognizerInfo? recognizer;
    private CancellationTokenSource? activePlayback;
    private bool disposed;
    public int OutputDeviceNumber { get; set; } = -1;
    public bool CanRecognize => recognizer is not null;
    public bool CanSpeak => speakerName is not null;
    public string RecognitionDescription { get; private set; } = "未检测到中文离线识别引擎，请在 Windows 语言选项中安装中文语音识别。";
    public string OutputDescription { get; private set; } = "未检测到中文系统声音，请安装中文语音包。";
    public event Action<string>? PlaybackStatus;

    public WindowsSpeech()
    {
        try
        {
            recognizer = SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault(r => r.Culture.Name == "zh-CN");
            if (recognizer is not null) RecognitionDescription = "中文 · 离线识别 · " + recognizer.Name;
        }
        catch (Exception e) { RecognitionDescription = "语音识别不可用：" + e.Message; }
        try
        {
            using var candidate = new SpeechSynthesizer();
            var voice = candidate.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.Name == "zh-CN");
            if (voice is null) return;
            speakerName = voice.VoiceInfo.Name;
            OutputDescription = "中文 · " + speakerName;
        }
        catch (Exception e) { OutputDescription = "播报不可用：" + e.Message; }
    }

    public SpeechRecognitionEngine CreateRecognizer(string wakeWord = "工程师", bool telemetryOnly = false, IReadOnlyList<string>? vocabulary = null)
    {
        if (recognizer is null) throw new InvalidOperationException(RecognitionDescription);
        var engine = new SpeechRecognitionEngine(recognizer);
        try
        {
            var phrases = vocabulary ?? TelemetryQueries.SpokenPhrases;
            // Keep the established command grammar stable as optional expressions are added.
            var standard = phrases.Where(TelemetryQueries.IsStandardPhrase);
            if (!telemetryOnly) standard = standard.Append(wakeWord);
            foreach (var group in new[] { standard, phrases.Where(p => !TelemetryQueries.IsStandardPhrase(p)) })
            {
                var options = group.ToArray();
                if (options.Length == 0) continue;
                var grammar = new GrammarBuilder { Culture = recognizer.Culture };
                grammar.Append(new Choices(options));
                engine.LoadGrammar(new Grammar(grammar));
            }
            if (telemetryOnly) return engine;
            // Racing terms are often homophones in general dictation (进站 / 进展).
            // Bias common strategy questions while retaining free dictation after the wake word.
            var strategy = new GrammarBuilder { Culture = recognizer.Culture };
            strategy.Append(wakeWord);
            strategy.Append(new Choices("我应该第几圈进站", "我应该什么时候进站", "现在应该换什么胎",
                "这套轮胎还能跑多久", "现在应该进站吗", "为什么胎温这么高"));
            engine.LoadGrammar(new Grammar(strategy));
            var aiGrammar = new GrammarBuilder { Culture = recognizer.Culture };
            aiGrammar.Append(wakeWord);
            aiGrammar.AppendDictation();
            engine.LoadGrammar(new Grammar(aiGrammar));
            // Dictation competes with the command grammar, so unrelated speech can be rejected rather than forced into a command.
            engine.LoadGrammar(new DictationGrammar());
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(3);
            engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(250);
            engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromMilliseconds(500);
            return engine;
        }
        catch { engine.Dispose(); throw; }
    }

    public async Task<VoiceResult> RecognizeAsync(byte[] wave, CancellationToken cancellationToken, string wakeWord = "工程师", float confidence = 0.35f, IReadOnlyList<string>? vocabulary = null)
    {
        var first = await RecognizePassAsync(wave, cancellationToken, wakeWord, confidence, false, vocabulary);
        if (first.Candidate is not { } candidate) return first;
        // Recheck the same audio without competition from free dictation, using the same threshold.
        var second = await RecognizePassAsync(wave, cancellationToken, wakeWord, confidence, true, vocabulary);
        return second.Text == candidate ? second : first;
    }

    private async Task<VoiceResult> RecognizePassAsync(byte[] wave, CancellationToken cancellationToken, string wakeWord, float confidence, bool telemetryOnly, IReadOnlyList<string>? vocabulary)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var engine = CreateRecognizer(wakeWord, telemetryOnly, vocabulary);
        using var audio = new MemoryStream(wave, writable: false);
        var results = new List<(string Text, float Confidence)>();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.SpeechRecognized += (_, e) => results.Add((e.Result.Text, e.Result.Confidence));
        string? rejected = null;
        engine.SpeechRecognitionRejected += (_, e) => rejected = e.Result?.Text ?? "";
        engine.RecognizeCompleted += (_, e) =>
        {
            if (e.Error is not null) done.TrySetException(e.Error);
            else done.TrySetResult();
        };
        engine.SetInputToWaveStream(audio);
        engine.RecognizeAsync(RecognizeMode.Multiple);
        try { await done.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken); }
        finally { engine.RecognizeAsyncCancel(); }
        cancellationToken.ThrowIfCancellationRequested();
        if (rejected is not null) return new(null, string.IsNullOrEmpty(rejected)
            ? "识别引擎未匹配到语句，请检查输入设备并重新讲话。"
            : $"引擎未确认“{rejected}”，请重说。");
        return VoiceCommandPolicy.Select(results, wakeWord, confidence, vocabulary);
    }

    public void Speak(string text, int volume = 80)
    {
        if (disposed) return;
        if (speakerName is null) { PlaybackStatus?.Invoke(OutputDescription); return; }
        StopSpeaking();
        _ = SpeakToDeviceAsync(ForSpeech(text), speakerName, volume, OutputDeviceNumber);
    }

    private async Task SpeakToDeviceAsync(string text, string name, int volume, int device)
    {
        using var cancellation = new CancellationTokenSource();
        activePlayback = cancellation;
        try
        {
            PlaybackStatus?.Invoke("工程师正在播报…");
            using var wave = new MemoryStream();
            using (var synth = new SpeechSynthesizer())
            {
                var generated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                synth.SelectVoice(name);
                synth.Volume = Math.Clamp(volume, 0, 100);
                synth.SetOutputToWaveStream(wave);
                synth.SpeakCompleted += (_, args) =>
                {
                    if (args.Error is not null) generated.TrySetException(args.Error);
                    else generated.TrySetResult();
                };
                synth.SpeakAsync(text);
                using var stopSynth = cancellation.Token.Register(synth.SpeakAsyncCancelAll);
                await generated.Task;
                cancellation.Token.ThrowIfCancellationRequested();
                synth.SetOutputToNull();
            }
            wave.Position = 0;
            using var reader = new WaveFileReader(wave);
            using var output = new WaveOutEvent { DeviceNumber = device };
            var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            output.PlaybackStopped += (_, args) =>
            {
                if (args.Exception is not null) ended.TrySetException(args.Exception);
                else ended.TrySetResult();
            };
            output.Init(reader);
            output.Play();
            using var stopPlayer = cancellation.Token.Register(output.Stop);
            await ended.Task;
            if (!cancellation.IsCancellationRequested) PlaybackStatus?.Invoke("播报完成");
        }
        catch (Exception e)
        {
            if (!cancellation.IsCancellationRequested) PlaybackStatus?.Invoke("播报失败，请检查输出设备：" + e.Message);
        }
        finally { if (activePlayback == cancellation) activePlayback = null; }
    }

    public static string ForSpeech(string text) => text.Replace("【演示数据】", "演示数据，")
        .Replace("imp gal/lap", "英制加仑每圈").Replace("imp gal", "英制加仑")
        .Replace("km/h", "公里每小时").Replace("mph", "英里每小时")
        .Replace("psi", "磅每平方英寸").Replace("kPa", "千帕")
        .Replace("°C", "摄氏度").Replace("°F", "华氏度")
        .Replace(" L/lap", " 升每圈").Replace(" L", " 升").Replace(" kg", " 千克").Replace(" lb", " 磅");
    public bool IsSpeaking => activePlayback != null;
    public void StopSpeaking() => activePlayback?.Cancel();
    public void Dispose() { disposed = true; StopSpeaking(); }
}

public static class VoiceCommandPolicy
{
    public static VoiceResult Select(IReadOnlyList<(string Text, float Confidence)> results, string wakeWord = "工程师", float threshold = 0.65f, IReadOnlyList<string>? vocabulary = null)
    {
        if (results.Count == 0) return new(null, "已收到音频，识别器未返回文字，请检查麦克风或延长按键时间。");
        var text = string.Join(" ", results.Select(r => r.Text)).Trim();
        var ai = AiRadio.AfterWakeWord(text, wakeWord) is not null;
        if (results.Count > 1 && !ai) return new(null, "识别到多段语句，一次请问一个问题。");
        var confidence = results.Min(r => r.Confidence);
        var local = ai ? null : TelemetrySpeechMatcher.Match(text, wakeWord, vocabulary ?? TelemetryQueries.SpokenPhrases);
        if (!float.IsFinite(confidence) || confidence < threshold)
            return new(null, $"听到“{text}”，置信度 {confidence:P1}，低于设置的 {threshold:P0}。", confidence,
                float.IsFinite(confidence) ? local?.Text : null);
        if (ai) return new(text, $"听到“{text}”", confidence);
        return local! with { Confidence = confidence };
    }
}
