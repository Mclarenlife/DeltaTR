using System.IO;
using System.Speech.Synthesis;
using DeltaTR.Engineer;
using DeltaTR.Hardware.Moza;
using DeltaTR.Telemetry.ACC;
using DeltaTR.Voice;

namespace DeltaTR.App.Diagnostics;

/// <summary>Explicit synthetic-audio screen check. Does not open a microphone or speaker.</summary>
internal static class WaveSmoke
{
    public static async Task RunAsync(string folder)
    {
        var log = Path.Combine(folder, "wave-smoke.log");
        void Record(string text) => File.AppendAllText(log, DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine);
        await using var bridge = new PitHouseBridge();
        var zip = new ZipformerSpeech();
        using var synth = new SpeechSynthesizer();
        synth.SelectVoice(synth.GetInstalledVoices().First(v => v.Enabled && v.VoiceInfo.Culture.Name == "zh-CN").VoiceInfo.Name);
        var queries = new TelemetryQueries();
        bridge.Start(BridgeSettings.LoadOrCreate());
        try
        {
            for (int cycle = 0; cycle < 6; cycle++)
            {
                bridge.Update(new("idle", "", cycle));
                await Task.Delay(5000);
                if (!bridge.Status.StartsWith("Pit House 已接收", StringComparison.Ordinal))
                    throw new IOException("Bridge not acknowledged: " + bridge.Status);
                string phrase = new[] { "当前轮胎温度多少", "还剩多少油", "排名" }[cycle % 3];
                using var pcm = new MemoryStream();
                synth.SetOutputToAudioStream(pcm, new(16000, System.Speech.AudioFormat.AudioBitsPerSample.Sixteen,
                    System.Speech.AudioFormat.AudioChannel.Mono));
                synth.Speak(phrase); synth.SetOutputToNull();
                var bytes = pcm.ToArray();
                var firstPreview = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                bridge.Update(new("listen", "请讲话…", cycle));
                using var turn = zip.Begin("工程师", [], queries.RecognitionPhrases, text =>
                {
                    bridge.Update(new("listen", text, cycle));
                    firstPreview.TrySetResult(text);
                });
                for (int offset = 0; offset < bytes.Length; offset += 3200)
                {
                    var chunk = new float[Math.Min(3200, bytes.Length - offset) / 2];
                    for (int n = 0; n < chunk.Length; n++) chunk[n] = BitConverter.ToInt16(bytes, offset + n * 2) / 32768f;
                    turn.Accept(chunk);
                    await Task.Delay(100);
                }
                turn.Accept(new float[12800]);
                string partial = await firstPreview.Task.WaitAsync(TimeSpan.FromSeconds(20));
                Record("Preview before release: " + partial);
                await Task.Delay(3000);
                var final = await turn.FinishAsync(CancellationToken.None);
                if (final.Text is null) throw new InvalidOperationException(final.Message);
                bridge.Update(new("think", final.Text, cycle));
                await Task.Delay(2000);
                var now = DateTimeOffset.UtcNow;
                var answer = queries.Answer(final.Text, SyntheticAdapter.Create(cycle, 0, now), now);
                if (!answer.HasVerifiedData) throw new InvalidOperationException("No synthetic telemetry answer");
                bridge.Update(new("reply", answer.Text, cycle));
                Record(final.Text + " => " + answer.Text);
                await Task.Delay(WheelScreen.ReadingDuration(answer.Text));
            }
            Record("PASS: synthetic audio preview -> confirmed input -> telemetry answer. Physical rendering needs user confirmation.");
        }
        finally { await bridge.StopAsync(); await zip.UnloadAsync(); }
    }
}
