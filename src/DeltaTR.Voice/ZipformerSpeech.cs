using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using DeltaTR.Engineer;

namespace DeltaTR.Voice;

public sealed class ZipformerSpeech
{
    private const string Encoder = "encoder-epoch-99-avg-1.int8.onnx";
    private const string Decoder = "decoder-epoch-99-avg-1.int8.onnx";
    private const string Joiner = "joiner-epoch-99-avg-1.int8.onnx";
    private static readonly string ModelDirectory = Path.Combine(AppContext.BaseDirectory, "models", "zipformer-zh-en");
    private readonly SemaphoreSlim access = new(1);
    private ZipformerNative.Model? model;
    public bool Installed { get; } = new[] { Encoder, Decoder, Joiner, "tokens.txt", "bpe.vocab" }
        .All(file => File.Exists(Path.Combine(ModelDirectory, file)));

    public ZipformerTurn Begin(string wakeWord, IEnumerable<string> customPhrases, IReadOnlyList<string> vocabulary, Action<string>? preview = null)
    {
        string[] racing = ["胎压", "胎温", "轮胎温度", "第几圈进站", "气温", "水温", "刹车", "左前", "右前", "左后", "右后", "进站", "圈速", "油量", "前车", "后车", "排名", "TC", "ABS"];
        var hotwords = string.Join('/', new[] { wakeWord }.Concat(customPhrases).Concat(racing)
            .Concat(vocabulary.Where(TelemetryQueries.IsStandardPhrase))
            .Where(s => s.Length > 0 && s.All(char.IsLetterOrDigit)).Select(s => s.ToUpperInvariant()).Distinct());
        return new ZipformerTurn((audio, token) => DecodeAsync(audio, hotwords, wakeWord, vocabulary, preview, token));
    }

    private async Task<VoiceResult> DecodeAsync(ChannelReader<float[]> audio, string hotwords, string wakeWord,
        IReadOnlyList<string> vocabulary, Action<string>? preview, CancellationToken token)
    {
        await access.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            model ??= LoadModel();
            using var stream = ZipformerNative.SherpaOnnxCreateOnlineStreamWithHotwords(model, hotwords);
            if (stream.IsInvalid) throw new InvalidOperationException("无法创建 Zipformer 识别流。");
            string previousPreview = "";
            void DecodeReady()
            {
                bool decoded = false;
                while (ZipformerNative.SherpaOnnxIsOnlineStreamReady(model, stream) != 0)
                {
                    token.ThrowIfCancellationRequested();
                    ZipformerNative.SherpaOnnxDecodeOnlineStream(model, stream);
                    decoded = true;
                }
                if (!decoded || preview is null) return;
                var json = ZipformerNative.SherpaOnnxGetOnlineStreamResultAsJson(model, stream);
                if (json == IntPtr.Zero) return;
                try
                {
                    using var result = JsonDocument.Parse(Marshal.PtrToStringUTF8(json)!);
                    string text = (result.RootElement.GetProperty("text").GetString() ?? "").Trim();
                    if (text != previousPreview && text.Length > 0) { previousPreview = text; preview(text); }
                }
                finally { ZipformerNative.SherpaOnnxDestroyOnlineStreamResultJson(json); }
            }
            await foreach (var samples in audio.ReadAllAsync(token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                ZipformerNative.SherpaOnnxOnlineStreamAcceptWaveform(stream, 16000, samples, samples.Length);
                DecodeReady();
            }
            // Supply right context for the last syllable when the driver releases PTT.
            var tail = new float[12800];
            ZipformerNative.SherpaOnnxOnlineStreamAcceptWaveform(stream, 16000, tail, tail.Length);
            ZipformerNative.SherpaOnnxOnlineStreamInputFinished(stream);
            DecodeReady();
            var json = ZipformerNative.SherpaOnnxGetOnlineStreamResultAsJson(model, stream);
            if (json == IntPtr.Zero) throw new InvalidOperationException("Zipformer 未返回识别结果。");
            try
            {
                using var result = JsonDocument.Parse(Marshal.PtrToStringUTF8(json)!);
                return Select(result.RootElement.GetProperty("text").GetString() ?? "", wakeWord, vocabulary);
            }
            finally { ZipformerNative.SherpaOnnxDestroyOnlineStreamResultJson(json); }
        }
        finally { access.Release(); }
    }

    public static VoiceResult Select(string transcript, string wakeWord, IReadOnlyList<string> vocabulary)
    {
        var text = transcript.Trim();
        if (text.Length == 0) return new(null, "Zipformer 未听到清晰语音，请重新讲话。");
        if (AiRadio.AfterWakeWord(text, wakeWord) is not null) return new(text, $"Zipformer 听到“{text}”");
        return TelemetrySpeechMatcher.Match(text, wakeWord, vocabulary);
    }

    private ZipformerNative.Model LoadModel()
    {
        if (!Installed) throw new FileNotFoundException("缺少 Zipformer 模型，请使用完整发布目录。");
        var config = new ZipformerNative.Config
        {
            SampleRate = 16000, Features = 80, DecodingMethod = "modified_beam_search", MaxPaths = 4, HotwordsScore = 1.0f,
            Model = new()
            {
                Encoder = Path.Combine(ModelDirectory, Encoder), Decoder = Path.Combine(ModelDirectory, Decoder),
                Joiner = Path.Combine(ModelDirectory, Joiner), Tokens = Path.Combine(ModelDirectory, "tokens.txt"),
                BpeVocab = Path.Combine(ModelDirectory, "bpe.vocab"), ModelingUnit = "cjkchar+bpe", Threads = 2, Provider = "cpu"
            }
        };
        var handle = ZipformerNative.SherpaOnnxCreateOnlineRecognizer(ref config);
        if (!handle.IsInvalid) return handle;
        handle.Dispose();
        throw new InvalidOperationException("Zipformer 加载失败，请检查发布目录中的模型和 Windows x64 运行库。");
    }

    public async Task UnloadAsync()
    {
        await access.WaitAsync().ConfigureAwait(false);
        try { model?.Dispose(); model = null; }
        finally { access.Release(); }
    }
}

public sealed class ZipformerTurn : IDisposable
{
    // The capture supplies at most 16 seconds of PCM, including while loading the model.
    private readonly Channel<float[]> input = Channel.CreateUnbounded<float[]>(new() { SingleReader = true });
    private readonly CancellationTokenSource stop = new();
    private readonly Task<VoiceResult> result;
    private int disposed;
    internal ZipformerTurn(Func<ChannelReader<float[]>, CancellationToken, Task<VoiceResult>> decode)
    {
        result = Task.Run(() => decode(input.Reader, stop.Token));
    }
    public void Accept(float[] samples) => input.Writer.TryWrite(samples);
    public Task<VoiceResult> FinishAsync(CancellationToken token)
    {
        input.Writer.TryComplete();
        return result.WaitAsync(token);
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        stop.Cancel();
        input.Writer.TryComplete();
        _ = result.ContinueWith(t => { _ = t.Exception; stop.Dispose(); }, TaskScheduler.Default);
    }
}
