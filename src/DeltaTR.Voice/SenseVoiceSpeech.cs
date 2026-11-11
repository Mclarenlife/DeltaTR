using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using DeltaTR.Engineer;
using NAudio.Wave;

namespace DeltaTR.Voice;

/// <summary>Bounded, local CPU recognition of a complete PTT utterance. No audio files or network requests.</summary>
public sealed partial class SenseVoiceSpeech
{
    private static readonly string ModelDirectory = Path.Combine(AppContext.BaseDirectory, "models", "sensevoice");
    private readonly SemaphoreSlim access = new(1);
    private SenseVoiceNative.Model? model;
    public bool Installed => File.Exists(Path.Combine(ModelDirectory, "model.int8.onnx")) &&
        File.Exists(Path.Combine(ModelDirectory, "tokens.txt"));

    public async Task<VoiceResult> RecognizeAsync(byte[] wave, string wakeWord, IReadOnlyList<string> vocabulary, CancellationToken token)
    {
        var transcript = await TranscribeAsync(wave, token).ConfigureAwait(false);
        return Select(transcript, wakeWord, vocabulary);
    }

    public Task<string> TranscribeAsync(byte[] wave, CancellationToken token) => Task.Run(async () =>
    {
        token.ThrowIfCancellationRequested();
        var samples = ReadSamples(wave);
        if (samples.All(s => Math.Abs(s) < 0.0001f)) return "";
        await access.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            model ??= LoadModel();
            token.ThrowIfCancellationRequested();
            using var stream = SenseVoiceNative.SherpaOnnxCreateOfflineStream(model);
            if (stream.IsInvalid) throw new InvalidOperationException("无法创建 SenseVoice 识别流。");
            SenseVoiceNative.SherpaOnnxAcceptWaveformOffline(stream, 16000, samples, samples.Length);
            SenseVoiceNative.SherpaOnnxDecodeOfflineStream(model, stream);
            token.ThrowIfCancellationRequested();
            var json = SenseVoiceNative.SherpaOnnxGetOfflineStreamResultAsJson(stream);
            if (json == IntPtr.Zero) throw new InvalidOperationException("SenseVoice 未返回识别结果。");
            try
            {
                using var result = JsonDocument.Parse(Marshal.PtrToStringUTF8(json)!);
                return CleanTranscript(result.RootElement.GetProperty("text").GetString() ?? "");
            }
            finally { SenseVoiceNative.SherpaOnnxDestroyOfflineStreamResultJson(json); }
        }
        finally { access.Release(); }
    }, token);

    public static string CleanTranscript(string text) => Tags().Replace(text, "").Trim();

    public static VoiceResult Select(string transcript, string wakeWord, IReadOnlyList<string> vocabulary)
    {
        var text = CleanTranscript(transcript);
        if (text.Length == 0) return new(null, "SenseVoice 未听到清晰语音，请重新讲话。");
        if (AiRadio.AfterWakeWord(text, wakeWord) is not null) return new(text, $"SenseVoice 听到“{text}”");
        var result = TelemetrySpeechMatcher.Match(text, wakeWord, vocabulary);
        return result with { Message = "SenseVoice · " + result.Message };
    }

    private static float[] ReadSamples(byte[] wave)
    {
        using var reader = new WaveFileReader(new MemoryStream(wave, writable: false));
        if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.BitsPerSample != 16 ||
            reader.WaveFormat.Channels != 1 || reader.WaveFormat.Encoding != WaveFormatEncoding.Pcm)
            throw new ArgumentException("SenseVoice 需要 16 kHz、16 位、单声道 PCM。", nameof(wave));
        if (reader.Length is < 2 or > 544000)
            throw new ArgumentException("语音输入为空或超过 17 秒。", nameof(wave));
        var bytes = new byte[(int)reader.Length];
        reader.ReadExactly(bytes);
        var samples = new float[bytes.Length / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, i * 2) / 32768f;
        return samples;
    }

    private SenseVoiceNative.Model LoadModel()
    {
        if (!Installed) throw new FileNotFoundException("缺少 SenseVoice 模型，请使用完整发布目录，或运行 scripts/install-sensevoice.ps1 后重新构建。");
        var config = new SenseVoiceNative.OfflineRecognizerConfig
        {
            feat_config = new() { sample_rate = 16000, feature_dim = 80 },
            model_config = new()
            {
                tokens = Path.Combine(ModelDirectory, "tokens.txt"), num_threads = 2, provider = "cpu", model_type = "sense_voice",
                sense_voice = new() { model = Path.Combine(ModelDirectory, "model.int8.onnx"), language = "zh", use_itn = 1 }
            },
            decoding_method = "greedy_search", max_active_paths = 4
        };
        var handle = SenseVoiceNative.SherpaOnnxCreateOfflineRecognizer(ref config);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();
        throw new InvalidOperationException("SenseVoice 模型加载失败，请检查模型文件和 Windows x64 运行库。");
    }

    public async Task UnloadAsync()
    {
        await access.WaitAsync().ConfigureAwait(false);
        try { model?.Dispose(); model = null; }
        finally { access.Release(); }
    }

    [GeneratedRegex(@"<\|[^>]*\|>", RegexOptions.CultureInvariant)]
    private static partial Regex Tags();
}

public enum SpeechEngine { Windows, Zipformer, SenseVoice }
