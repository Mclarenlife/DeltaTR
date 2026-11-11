using System.Diagnostics;
using System.IO;
using System.Speech.Synthesis;
using System.Text.Json;
using DeltaTR.Core;
using DeltaTR.Engineer;
using DeltaTR.Telemetry.ACC;
using DeltaTR.Voice;

namespace DeltaTR.App.Diagnostics;

/// <summary>Reproducible synthetic audio comparison. Does not record a microphone or represent real-driver accuracy.</summary>
internal static class SpeechBenchmark
{
    public static async Task RunAsync(string folder)
    {
        using var synth = new SpeechSynthesizer();
        synth.SelectVoice(synth.GetInstalledVoices().First(v => v.Enabled && v.VoiceInfo.Culture.Name == "zh-CN").VoiceInfo.Name);
        var sense = new SenseVoiceSpeech();
        var zip = new ZipformerSpeech();
        var overrides = new Dictionary<string, string[]> { ["airTemperature"] = ["报告气温"] };
        var query = new TelemetryQueries(overrides, "队长");
        var iracing = new TelemetryQueries(overrides, "队长", RacingGame.IRacing);
        var literalVocabulary = query.RecognitionPhrases.ToArray();
        static string Words(string value) => string.Concat(value.Where(char.IsLetterOrDigit));
        var rows = new List<object>();
        var failures = new List<string>();
        var now = DateTimeOffset.UtcNow;
        var snapshot = SyntheticAdapter.Create(1, 0, now);
        (string Phrase, string? Field, bool Ai)[] cases =
        [
            ("胎温", "temperature.FL", false), ("胎温多少", "temperature.FL", false), ("当前轮胎温度多少", "temperature.FL", false),
            ("胎温多少多少", "temperature.FL", false), ("胎温多少少", "temperature.FL", false),
            ("右后胎压多少", "pressure.RR", false), ("报告气温", "airTemperature", false),
            ("帮我看看四条胎各有多热", "temperature.FL", false), ("麻烦看一下左前胎现在多少度", "temperature.FL", false),
            ("队长，我应该第几圈进站", null, true), ("不要告诉我油量", null, false), ("打开音乐", null, false)
        ];
        try
        {
            foreach (var item in cases)
            {
                using var pcm = new MemoryStream();
                synth.SetOutputToAudioStream(pcm, new(16000, System.Speech.AudioFormat.AudioBitsPerSample.Sixteen,
                    System.Speech.AudioFormat.AudioChannel.Mono));
                synth.Speak(item.Phrase); synth.SetOutputToNull();
                foreach (var noisy in new[] { false, true })
                {
                    var bytes = pcm.ToArray();
                    var random = new Random(42);
                    var peak = 0;
                    var samples = new float[bytes.Length / 2];
                    for (var i = 0; i < samples.Length; i++)
                    {
                        var sample = BitConverter.ToInt16(bytes, i * 2);
                        if (noisy) sample = (short)Math.Clamp(sample / 4 + 100 * Math.Sin(2 * Math.PI * 80 * i / 16000) + random.Next(-100, 101), short.MinValue, short.MaxValue);
                        BitConverter.TryWriteBytes(bytes.AsSpan(i * 2, 2), sample);
                        peak = Math.Max(peak, Math.Abs((int)sample));
                        samples[i] = sample / 32768f;
                    }
                    bool Correct(VoiceResult result)
                    {
                        if (item.Ai) return AiRadio.AfterWakeWord(result.Text ?? "", "队长")?.Contains("进站") == true;
                        if (item.Field is null) return result.Text is null;
                        if (result.Text is null) return false;
                        var keys = query.Answer(result.Text, snapshot, now).EvidenceFields;
                        return item.Field == "temperature.FL" && !item.Phrase.Contains("左前")
                            ? keys.SequenceEqual(Metrics.Wheels.Select(w => "temperature." + w))
                            : keys.SequenceEqual([item.Field]);
                    }
                    var watch = Stopwatch.StartNew();
                    var transcript = await sense.TranscribeAsync(MicrophoneCapture.PrepareWave(bytes, peak * 100d / 32768), CancellationToken.None);
                    var result = SenseVoiceSpeech.Select(transcript, "队长", query.RecognitionPhrases);
                    if (item.Phrase == "胎温多少")
                    {
                        var ir = SenseVoiceSpeech.Select(transcript, "队长", iracing.RecognitionPhrases);
                        if (ir.Text != "胎温多少" || ir.DisplayMessage != "已识别“胎温多少”")
                            failures.Add("iRacing 胎温多少 -> " + ir.Message);
                    }
                    var elapsed = watch.ElapsedMilliseconds;
                    var correct = Correct(result);
                    rows.Add(new { Engine = "SenseVoice", item.Phrase, Noisy = noisy, Transcript = transcript, result.Text, result.Message,
                        ExactTranscript = Words(item.Phrase) == Words(transcript),
                        LiteralRoutingCorrect = Correct(SenseVoiceSpeech.Select(transcript, "队长", literalVocabulary)),
                        Correct = correct, Milliseconds = elapsed });
                    if (!noisy && !correct) failures.Add(item.Phrase + " -> " + result.Message);
                    watch.Restart();
                    using var turn = zip.Begin("队长", overrides.Values.SelectMany(p => p), query.RecognitionPhrases);
                    turn.Accept(samples);
                    var previous = await turn.FinishAsync(CancellationToken.None);
                    var checkedResult = await sense.RecheckTelemetryAsync(previous, MicrophoneCapture.PrepareWave(bytes, peak * 100d / 32768),
                        "队长", query.RecognitionPhrases, CancellationToken.None);
                    rows.Add(new { Engine = "Zipformer", item.Phrase, Noisy = noisy, previous.Transcript, previous.Text, previous.Message,
                        ExactTranscript = Words(item.Phrase) == Words(previous.Transcript ?? ""),
                        LiteralRoutingCorrect = Correct(ZipformerSpeech.Select(previous.Transcript ?? "", "队长", literalVocabulary)),
                        BeforeRecheckCorrect = Correct(previous), Correct = Correct(checkedResult), FinalMessage = checkedResult.Message,
                        Milliseconds = watch.ElapsedMilliseconds });
                }
            }
            var silent = await sense.RecognizeAsync(MicrophoneCapture.PrepareWave(new byte[32000], 0), "队长", query.RecognitionPhrases, CancellationToken.None);
            if (silent.Text is not null) failures.Add("Silence produced a command");
        }
        finally
        {
            await sense.UnloadAsync(); await zip.UnloadAsync();
            await File.WriteAllTextAsync(Path.Combine(folder, "speech-benchmark.json"), JsonSerializer.Serialize(
                new { SyntheticAudioOnly = true, Rows = rows, Failures = failures }, new JsonSerializerOptions { WriteIndented = true }));
        }
        if (failures.Count > 0) throw new InvalidOperationException("SenseVoice clean-audio regression: " + string.Join("; ", failures));
    }
}
