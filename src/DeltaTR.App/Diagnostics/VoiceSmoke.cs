using System.IO;
using System.Speech.Synthesis;
using DeltaTR.Core;
using DeltaTR.Engineer;
using DeltaTR.Telemetry.ACC;
using DeltaTR.Voice;

namespace DeltaTR.App.Diagnostics;

/// <summary>Opt-in real Chinese TTS -> audio -> ASR -> telemetry -> TTS integration, with no microphone or speaker playback.</summary>
internal static class VoiceSmoke
{
    private static async Task TestZipformerAsync(SpeechSynthesizer synth, string folder)
    {
        var zip = new ZipformerSpeech();
        var phrases = new Dictionary<string, string[]> { ["airTemperature"] = ["报告气温"] };
        var queries = new TelemetryQueries(phrases, "队长");
        try
        {
            foreach (var phrase in new[] { "当前轮胎温度多少", "报告气温", "队长，我应该第几圈进站", "打开音乐" })
            {
                using var pcm = new MemoryStream();
                var format = new System.Speech.AudioFormat.SpeechAudioFormatInfo(16000,
                    System.Speech.AudioFormat.AudioBitsPerSample.Sixteen, System.Speech.AudioFormat.AudioChannel.Mono);
                synth.SetOutputToAudioStream(pcm, format); synth.Speak(phrase); synth.SetOutputToNull();
                var bytes = pcm.ToArray();
                var preview = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var turn = zip.Begin("队长", phrases.Values.SelectMany(x => x), queries.RecognitionPhrases,
                    text => preview.TrySetResult(text));
                for (var offset = 0; offset < bytes.Length; offset += 1600)
                {
                    var chunk = new float[Math.Min(1600, bytes.Length - offset) / 2];
                    for (var i = 0; i < chunk.Length; i++) chunk[i] = BitConverter.ToInt16(bytes, offset + i * 2) / 32768f;
                    turn.Accept(chunk);
                }
                // Keep the stream open: preview must arrive before releasing PTT/FinishAsync.
                turn.Accept(new float[12800]);
                var partial = await preview.Task.WaitAsync(TimeSpan.FromSeconds(20));
                await File.AppendAllTextAsync(Path.Combine(folder, "zipformer.log"), $"LIVE before finish: {partial}\n");
                var result = await turn.FinishAsync(CancellationToken.None);
                await File.AppendAllTextAsync(Path.Combine(folder, "zipformer.log"), $"{phrase} -> {result.Text} {result.Message}\n");
                var now = DateTimeOffset.UtcNow;
                var valid = phrase switch
                {
                    "当前轮胎温度多少" => result.Text != null && queries.Answer(result.Text, SyntheticAdapter.Create(1, 0, now), now).EvidenceFields.Contains("temperature.FL"),
                    "报告气温" => result.Text != null && queries.Answer(result.Text, SyntheticAdapter.Create(1, 0, now), now).EvidenceFields.Contains("airTemperature"),
                    "打开音乐" => result.Text == null,
                    _ => AiRadio.AfterWakeWord(result.Text ?? "", "队长")?.Contains("进站") == true
                };
                if (!valid) throw new InvalidOperationException("Zipformer audio routing failed: " + result.Message);
            }
        }
        finally { await zip.UnloadAsync(); }
    }

    public static async Task RunAsync(string folder)
    {
        var log = new List<string>();
        using var speech = new WindowsSpeech();
        log.Add(speech.RecognitionDescription); log.Add(speech.OutputDescription);
        await File.WriteAllLinesAsync(Path.Combine(folder, "voice-smoke.log"), log);
        if (!speech.CanRecognize || !speech.CanSpeak) throw new InvalidOperationException("中文语音引擎未安装，不能执行语音集成测试。");
        using var synth = new SpeechSynthesizer();
        synth.SelectVoice(synth.GetInstalledVoices().First(v => v.Enabled && v.VoiceInfo.Culture.Name == "zh-CN").VoiceInfo.Name);
        await TestZipformerAsync(synth, folder);
        var passed = 0;
        foreach (var phrase in new[] { "刹车温度多少", "水温多少", "轮胎温度多少", "现在胎压怎么样", "还剩多少油", "我现在第几", "我领先后车多少秒", "右后胎几度", "下雨吗", "车辆状况", "赛场状况", "排名", "胎温", "前车差多少", "打开音乐", "不要告诉我油量" })
        {
            using var input = new MemoryStream();
            synth.SetOutputToWaveStream(input); synth.Speak(phrase); synth.SetOutputToNull();
            var result = await speech.RecognizeAsync(input.ToArray(), CancellationToken.None);
            if (result.Text != phrase)
            {
                log.Add($"REFUSED {phrase} -> {result.Message} confidence={result.Confidence}");
                if (result.Text is not null) throw new InvalidOperationException($"Wrong command: {phrase} -> {result.Text}");
                continue;
            }
            var now = DateTimeOffset.UtcNow;
            var reply = new TelemetryQueries().Answer(result.Text, SyntheticAdapter.Create(0, 0, now), now);
            if (!reply.HasVerifiedData || !reply.Text.Contains("【演示数据】")) throw new InvalidOperationException("Voice telemetry answer failed");
            using var output = new MemoryStream();
            synth.SetOutputToWaveStream(output); synth.Speak(WindowsSpeech.ForSpeech(reply.Text)); synth.SetOutputToNull();
            if (output.Length < 1000) throw new InvalidOperationException("No TTS waveform");
            passed++;
            log.Add($"PASS {phrase} ({result.Confidence}) -> {reply.Text} -> PCM {output.Length} bytes");
        }
        using (var raw = new MemoryStream())
        {
            var format = new System.Speech.AudioFormat.SpeechAudioFormatInfo(16000,
                System.Speech.AudioFormat.AudioBitsPerSample.Sixteen, System.Speech.AudioFormat.AudioChannel.Mono);
            synth.SetOutputToAudioStream(raw, format);
            synth.Speak("当前轮胎温度多少");
            synth.SetOutputToNull();
            var pcm = raw.ToArray();
            var peak = 0;
            for (var i = 0; i + 1 < pcm.Length; i += 2)
            {
                var sample = (short)(BitConverter.ToInt16(pcm, i) / 8);
                peak = Math.Max(peak, Math.Abs((int)sample));
                BitConverter.TryWriteBytes(pcm.AsSpan(i, 2), sample);
            }
            var percent = peak * 100d / 32768;
            // Run the same soft utterance through both effects with a reproducible fan/rumble input.
            var noiseTest = new short[16000 + pcm.Length / 2];
            var rng = new Random(42);
            for (var i = 0; i < noiseTest.Length; i++)
            {
                var voice = i < 16000 ? 0 : BitConverter.ToInt16(pcm, (i - 16000) * 2);
                noiseTest[i] = (short)(voice + 70 * Math.Sin(2 * Math.PI * 60 * i / 16000) + rng.Next(-35, 36));
            }
            var effects = new SpeechInputProcessor(true, true);
            for (var i = 0; i < noiseTest.Length; i += 800)
                effects.Process(noiseTest.AsSpan(i, Math.Min(800, noiseTest.Length - i)));
            var processed = new byte[noiseTest.Length * 2];
            Buffer.BlockCopy(noiseTest, 0, processed, 0, processed.Length);
            var enhanced = await speech.RecognizeAsync(MicrophoneCapture.PrepareWave(processed, 0, normalizeQuietInput: false), CancellationToken.None);
            if (enhanced.Text != "当前轮胎温度多少") throw new InvalidOperationException("Processed noisy speech failed: " + enhanced.Message);
            log.Add($"PASS enhanced and denoised soft speech -> {enhanced.Text} ({enhanced.Confidence:P1})");
            var prepared = MicrophoneCapture.PrepareWave(pcm, percent);
            var result = await speech.RecognizeAsync(prepared, CancellationToken.None);
            if (result.Text != "当前轮胎温度多少" || !new TelemetryQueries().Answer(result.Text, SyntheticAdapter.Create(0, 0, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow).HasVerifiedData)
                throw new InvalidOperationException("Current tyre temperature question failed: " + result.Message);
            log.Add($"PASS 16 kHz low input {percent:0.0}% -> {result.Text}, confidence {result.Confidence:P1}");
        }
        using (var customAudio = new MemoryStream())
        {
            var library = new TelemetryQueries(new Dictionary<string, string[]> { ["airTemperature"] = ["报告气温"] }, "工程师");
            synth.SetOutputToWaveStream(customAudio); synth.Speak("报告气温"); synth.SetOutputToNull();
            var heard = await speech.RecognizeAsync(customAudio.ToArray(), CancellationToken.None, vocabulary: library.RecognitionPhrases);
            var now = DateTimeOffset.UtcNow;
            if (heard.Text != "报告气温" || !library.Answer(heard.Text, SyntheticAdapter.Create(0, 0, now), now).EvidenceFields.Contains("airTemperature"))
                throw new InvalidOperationException("Custom spoken alias failed: " + heard.Message);
            log.Add($"PASS custom spoken alias {heard.Text} -> airTemperature ({heard.Confidence:P1})");
        }
        foreach (var word in new[] { "工程师", "队长" })
        {
            using var audio = new MemoryStream();
            synth.SetOutputToWaveStream(audio);
            synth.Speak(word + "，我应该第几圈进站"); synth.SetOutputToNull();
            var result = await speech.RecognizeAsync(audio.ToArray(), CancellationToken.None, word);
            var question = result.Text is null ? null : AiRadio.AfterWakeWord(result.Text, word);
            log.Add($"WAKE {word} -> {result.Text} ({result.Confidence}) {result.Message}");
            await File.WriteAllLinesAsync(Path.Combine(folder, "voice-smoke.log"), log);
            if (question?.Replace(" ", "") != "我应该第几圈进站") throw new InvalidOperationException("Wake recognition failed for " + word);
        }
        await File.WriteAllLinesAsync(Path.Combine(folder, "voice-smoke.log"), log);
        var outputs = AudioDevice.List(false);
        if (outputs.Count > 1)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            speech.PlaybackStatus += message =>
            {
                if (message == "播报完成") completion.TrySetResult();
                if (message.Contains("失败")) completion.TrySetException(new InvalidOperationException(message));
            };
            speech.OutputDeviceNumber = outputs[1].Number;
            speech.Speak("测试打断上一句", 0);
            speech.StopSpeaking();
            speech.Speak("收到", 0);
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
            log.Add("PASS muted playback on selected output after interruption");
            await File.WriteAllLinesAsync(Path.Combine(folder, "voice-smoke.log"), log);
        }
        if (passed < 4) throw new InvalidOperationException("Too few speech integration cases passed; inspect voice-smoke.log");
    }
}
