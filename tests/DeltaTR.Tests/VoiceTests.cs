using DeltaTR.Core;
using DeltaTR.Engineer;
using DeltaTR.Telemetry.ACC;
using DeltaTR.Voice;

internal static class VoiceTests
{
    public static void Register(List<(string Name, Func<Task> Run)> cases, DateTimeOffset now)
    {
        void Test(string name, Action action) => cases.Add(("Voice / " + name, () => { action(); return Task.CompletedTask; }));
        void Check(bool condition) { if (!condition) throw new Exception("Voice assertion failed"); }
        Test("Chinese word spacing is normalized", () => Check(VoiceCommandPolicy.Select([("现在 胎压 怎么样。", .9f)]).Text == "现在胎压怎么样"));
        Test("input processing bypass, quiet speech gain and loud peak headroom", () =>
        {
            var quiet = Enumerable.Range(0, 8000).Select(i => (short)(600 * Math.Sin(i * 0.12))).ToArray();
            var output = quiet.ToArray();
            new SpeechInputProcessor(false, false).Process(output);
            Check(output.SequenceEqual(quiet));
            var boost = new SpeechInputProcessor(true, false);
            boost.Process(output);
            Check(output.Max() > 2 * quiet.Max());
            short[] loud = [20000, -20000, 32000, -32000];
            boost.Process(loud);
            Check(loud.SequenceEqual(new short[] { 20000, -20000, 32000, -32000 }));
        });
        Test("noise suppression reduces sustained fan noise and preserves silence", () =>
        {
            var random = new Random(25);
            var input = Enumerable.Range(0, 48000).Select(i =>
                (short)(350 * Math.Sin(i * Math.PI / 160) + random.Next(-250, 251))).ToArray();
            var output = input.ToArray();
            var processor = new SpeechInputProcessor(false, true);
            for (var i = 0; i < output.Length; i += 800) processor.Process(output.AsSpan(i, 800));
            double Energy(short[] signal) => signal.TakeLast(8000).Average(x => (double)x * x);
            Check(Energy(output) < 0.4 * Energy(input));
            var silence = new short[800];
            new SpeechInputProcessor(true, true).Process(silence);
            Check(silence.All(x => x == 0));
        });
        Test("meter and PTT gate compare unrounded peaks", () =>
        {
            Check(!MicrophoneCapture.MeetsInputThreshold(1.99, 2));
            Check(MicrophoneCapture.MeetsInputThreshold(2, 2));
            Check(!MicrophoneCapture.MeetsInputThreshold(0, 0));
            Check(MicrophoneCapture.MeetsInputThreshold(0.1, 0));
            Check(MicrophoneCapture.MeetsInputThreshold(100, 100));
        });
        Test("isolated button clicks fail the duration gate but a quiet short syllable passes", () =>
        {
            var samples = new short[3200];
            samples[159] = short.MaxValue;
            samples[160] = short.MaxValue;
            samples[2559] = short.MaxValue;
            Check(!MicrophoneCapture.HasSustainedInput(samples, 2));
            for (int i = 650; i < 1450; i++) samples[i] = (short)(800 * Math.Sin(i * 0.1));
            Check(MicrophoneCapture.HasSustainedInput(samples, 2));
        });
        Test("Zipformer transcript routes by saved aliases or wake prefix without inventing confidence", () =>
        {
            string[] custom = ["报一下路温","TC几档"];
            Check(ZipformerSpeech.Select("报 一下 路温。", "队长", custom).Text == custom[0]);
            Check(ZipformerSpeech.Select("报一一下路温", "队长", custom).Text == custom[0]);
            Check(ZipformerSpeech.Select("队队长，进站", "队长", custom).Text is null);
            Check(ZipformerSpeech.Select("tc 几档", "队长", custom).Confidence is null);
            Check(ZipformerSpeech.Select("队 长，我应该什么时候进站", "队长", custom).Text is not null);
            Check(ZipformerSpeech.Select("工程师，我应该什么时候进站", "队长", custom).Text is null);
            Check(ZipformerSpeech.Select("不要报一下路温", "队长", custom).Text is null);
            Check(ZipformerSpeech.Select("   ", "队长", custom).Text is null);
        });
        Test("SenseVoice cleans metadata and preserves vocabulary, wake word and refusal boundaries", () =>
        {
            string[] custom = ["报告气温", "胎温"];
            Check(SenseVoiceSpeech.Select("<|zh|><|NEUTRAL|><|Speech|>报告气温。", "队长", custom).Text == "报告气温");
            Check(SenseVoiceSpeech.Select("台温", "队长", custom).Text == "胎温");
            Check(SenseVoiceSpeech.Select("队长，我应该第几圈进站？", "队长", custom).Text is not null);
            Check(SenseVoiceSpeech.Select("工程师，我应该第几圈进站？", "队长", custom).Text is null);
            Check(SenseVoiceSpeech.Select("不要告诉我胎温", "队长", custom).Text is null);
            Check(SenseVoiceSpeech.Select("打开音乐", "队长", custom).Text is null);
            Check(SenseVoiceSpeech.Select("<|zh|><|nospeech|>", "队长", custom).Text is null);
            Check(SenseVoiceSpeech.Select("胎温", "队长", custom).Confidence is null);
        });
        cases.Add(("Voice / SenseVoice silence and canceled calls never load a model", async () =>
        {
            var engine = new SenseVoiceSpeech();
            var silence = MicrophoneCapture.PrepareWave(new byte[32000], 0);
            Check(await engine.TranscribeAsync(silence, CancellationToken.None) == "");
            using var stop = new CancellationTokenSource(); stop.Cancel();
            try { await engine.TranscribeAsync(silence, stop.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { }
            await engine.UnloadAsync();
        }));
        Test("all engines repair racing terms only into the intended current telemetry field", () =>
        {
            var q = new TelemetryQueries();
            var frame = SyntheticAdapter.Create(1, 0, now);
            foreach (var (heard, target) in new[] { ("台温", "temperature.FL"), ("当前台温多少", "temperature.RR"),
                ("左前抬温多少", "temperature.FL"), ("右后台压多少", "pressure.RR"), ("气温", "airTemperature") })
            {
                var zip = ZipformerSpeech.Select(heard, "工程师", q.RecognitionPhrases);
                Check(SenseVoiceSpeech.Select(heard, "工程师", q.RecognitionPhrases).Text == zip.Text);
                var windows = VoiceCommandPolicy.Select([(heard, .8f)], "工程师", .35f, q.RecognitionPhrases);
                Check(zip.Text != null && zip.Text == windows.Text && zip.Message.Contains(heard) && windows.Message.Contains(heard));
                var keys = q.Answer(zip.Text!, frame, now).EvidenceFields;
                Check(keys.Contains(target));
                if (heard.StartsWith("左前") || heard.StartsWith("右后")) Check(keys.Length == 1);
            }
        });
        Test("tyre homophones show the same corrected question in previews, accepted status and telemetry", () =>
        {
            foreach (var game in new[] { RacingGame.ACC, RacingGame.LMU })
            {
                var q = new TelemetryQueries(new Dictionary<string, string[]>(), "工程师", game);
                var words = q.RecognitionPhrases;
                foreach (var heard in new[] { "台温多少", "台 温 多 少？" })
                {
                    Check(TelemetrySpeechMatcher.DisplayQuestion(heard, "工程师", words) == "胎温多少");
                    foreach (var result in new[] { ZipformerSpeech.Select(heard, "工程师", words),
                        SenseVoiceSpeech.Select(heard, "工程师", words), VoiceCommandPolicy.Select([(heard,.9f)], "工程师", .35f, words) })
                    {
                        Check(result.Text == "胎温多少" && result.DisplayMessage == "已识别“胎温多少”");
                        Check(q.Answer(result.Text!, SyntheticAdapter.Create(1, 0, now) with { Game = game }, now)
                            .EvidenceFields.SequenceEqual(Metrics.Wheels.Select(w => "temperature." + w)));
                    }
                }
                foreach (var literal in new[] { "平台温度", "工程师，台温多少", "不要报台温多少" })
                    Check(TelemetrySpeechMatcher.DisplayQuestion(literal, "工程师", words) == literal);
            }
            var custom = new TelemetryQueries(new Dictionary<string, string[]> { ["airTemperature"] = ["台温多少"] }, "工程师");
            Check(TelemetrySpeechMatcher.DisplayQuestion("台温多少", "工程师", custom.RecognitionPhrases) == "台温多少");
            Check(SenseVoiceSpeech.Select("台温多少", "工程师", custom.RecognitionPhrases).Text == "台温多少");
        });
        Test("iRacing tyre question reaches last measured tyres and labels the answer", () =>
        {
            var q = new TelemetryQueries(new Dictionary<string, string[]>(), "工程师", RacingGame.IRacing);
            var frame = SyntheticAdapter.Create(1, 0, now) with { Game = RacingGame.IRacing };
            foreach (var wheel in Metrics.Wheels)
                frame.Fields["pitTemp." + wheel] = frame.Fields["temperature." + wheel];
            foreach (var result in new[] { SenseVoiceSpeech.Select("台温多少？", "工程师", q.RecognitionPhrases),
                ZipformerSpeech.Select("台温多少", "工程师", q.RecognitionPhrases),
                VoiceCommandPolicy.Select([("台温多少", .8f)], "工程师", .35f, q.RecognitionPhrases) })
            {
                Check(result.Text == "胎温多少" && result.DisplayMessage == "已识别“胎温多少”");
                var reply = q.Answer(result.Text!, frame, now);
                Check(reply.Text.Contains("上次测量") && reply.HasVerifiedData &&
                    reply.EvidenceFields.SequenceEqual(Metrics.Wheels.Select(w => "pitTemp." + w)));
            }
            Check(TelemetrySpeechMatcher.DisplayQuestion("台温多少", "工程师", q.RecognitionPhrases) == "胎温多少");
            Check(q.Answer("右后台温多少", frame, now).EvidenceFields.SequenceEqual(["pitTemp.RR"]));
            var current = q.Answer("当前台温多少", frame, now);
            Check(!current.HasVerifiedData && current.Text.Contains("上次胎温多少"));
            frame.Fields.Remove("pitTemp.RR");
            Check(!q.Answer("台温多少", frame, now).HasVerifiedData);
        });
        Test("ambiguous repairs, negative questions and near wake words are rejected", () =>
        {
            string[] custom = ["胎温", "台温", "工程师台温", "左前胎温多少"];
            Check(ZipformerSpeech.Select("台温", "工程师", custom).Text == "台温");
            foreach (var heard in new[] { "抬温", "不要报台温", "后左台温多少", "胎纹", "工成师台温" })
            {
                Check(ZipformerSpeech.Select(heard, "工程师", custom).Text == null);
                Check(VoiceCommandPolicy.Select([(heard, .8f)], "工程师", .35f, custom).Text == null);
            }
            Check(ZipformerSpeech.Select("台温", "队长", ["水温多少"]).Text == null);
            Check(ZipformerSpeech.Select("工程师，看看台温", "工程师", custom).Text == "工程师，看看台温");
            var quiet = VoiceCommandPolicy.Select([("台温", .2f)], "队长", .35f, ["胎温"]);
            Check(quiet.Text == null && quiet.Candidate == "胎温");
        });
        Test("PTT transcript echoes resolve once with the original wheel, quantity and confidence", () =>
        {
            var queries = new TelemetryQueries();
            var vocabulary = queries.RecognitionPhrases;
            var frame = SyntheticAdapter.Create(1, 0, now);
            foreach (var (heard, clean) in new[] {
                ("胎温多少多少", "胎温多少"), ("台温多少少", "胎温多少"), ("台温多少手？", "胎温多少"),
                ("左前台温多少，多少？", "左前胎温多少"), ("胎温多少？胎温多少。", "胎温多少"),
                ("请问", ""), ("前台温控多少多少", ""), ("台温多少不要", ""),
                ("左前胎温多少，右后胎温多少", ""), ("气温多少，胎温多少", ""),
                ("工成师，台温多少多少", ""), ("胎温多少轰隆", "") })
            {
                foreach (var result in new[] { ZipformerSpeech.Select(heard, "工程师", vocabulary),
                    SenseVoiceSpeech.Select(heard, "工程师", vocabulary),
                    VoiceCommandPolicy.Select([(heard, .9f)], "工程师", .35f, vocabulary) })
                {
                    if (clean.Length == 0) Check(result.Text == null);
                    else Check(result.Text != null && result.Message.Contains(heard) &&
                        queries.Answer(result.Text, frame, now).EvidenceFields.SequenceEqual(queries.Answer(clean, frame, now).EvidenceFields));
                }
            }
            foreach (var tail in new[] { "多少", "少", "胎温多少" })
                Check(VoiceCommandPolicy.Select([("胎温多少", .9f), (tail, .8f)], "工程师", .35f, vocabulary).Text == "胎温多少");
            Check(VoiceCommandPolicy.Select([("胎温多少", .9f), ("多少", .1f)], "工程师", .35f, vocabulary).Text == null);
            Check(ZipformerSpeech.Select("台温多少多少", "队长", ["油量"]).Text == null);
            Check(ZipformerSpeech.Select("台温多少多少", "队长", ["台温多少", "胎温多少"]).Text == "台温多少");
            string threshold = "左前胎压超过26.55psi";
            Check(ZipformerSpeech.Select(threshold + "，" + threshold, "工程师", vocabulary).Text == threshold);
            Check(ZipformerSpeech.Select("工程师，胎温多少多少", "工程师", vocabulary).Text == "工程师，胎温多少多少");
        });
        Test("SenseVoice drops non-speech results even when they contain plausible commands", () =>
        {
            using var background = System.Text.Json.JsonDocument.Parse("""{"lang":"<|zh|>","event":"<|BGM|>","text":"胎温多少"}""");
            Check(SenseVoiceSpeech.ReadTranscript(background.RootElement) == "");
            Check(SenseVoiceSpeech.Select("<|nospeech|>胎温多少", "工程师", ["胎温多少"]).Text == null);
            Check(SenseVoiceSpeech.Select("<|Cough|>工程师进站", "工程师", ["胎温多少"]).Text == null);
            Check(SenseVoiceSpeech.Select("<|zh|><|Speech_Noise|>胎温多少", "工程师", ["胎温多少"]).Text == "胎温多少");
        });
        Test("conversational readings keep wheel scope and work across recognition engines", () =>
        {
            var q = new TelemetryQueries();
            var frame = SyntheticAdapter.Create(1, 0, now);
            foreach (var (phrase, keys) in new (string, string[])[] {
                ("帮我看看四条胎各有多热", Metrics.Wheels.Select(w => "temperature." + w).ToArray()),
                ("左前胎现在多少个 PSI？", ["pressure.FL"]), ("麻烦看一下右后胎现在多少度", ["temperature.RR"]),
                ("请帮我查一下目前排名多少", ["position"]) })
            {
                foreach (var result in new[] { ZipformerSpeech.Select(phrase, "工程师", q.RecognitionPhrases),
                    SenseVoiceSpeech.Select(phrase, "工程师", q.RecognitionPhrases),
                    VoiceCommandPolicy.Select([(phrase, .8f)], "工程师", .35f, q.RecognitionPhrases) })
                    Check(result.Text != null && q.Answer(result.Text, frame, now).EvidenceFields.SequenceEqual(keys));
            }
            var unclear = q.Answer("帮我看看左前胎多少", frame, now);
            Check(!unclear.HasVerifiedData && unclear.WheelToClarify == "左前");
            Check(q.Answer(q.CompleteWheelQuestion("左前", "胎压"), frame, now).EvidenceFields.SequenceEqual(["pressure.FL"]));
            Check(q.CompleteWheelQuestion("左前", "排名") == "排名");
            foreach (var bad in new[] { "不要告诉我左前胎多少度", "如果左前胎压低于26就进站", "左前和右后胎压是多少", "我是不是应该降低胎压", "工成师左前胎多少" })
                Check(ZipformerSpeech.Select(bad, "工程师", q.RecognitionPhrases).Text == null);
            var ir = new TelemetryQueries(new Dictionary<string, string[]>(), "工程师", RacingGame.IRacing);
            Check(!ir.Answer("帮我看看四条胎各有多热", frame, now).HasVerifiedData);
            var custom = new TelemetryQueries(new Dictionary<string, string[]> { ["pressure.FL"] = ["前掌读数"] }, "工程师");
            Check(custom.Answer("请帮我查一下前掌读数", frame, now).EvidenceFields.SequenceEqual(["pressure.FL"]));
            Check(ZipformerSpeech.Select("左前胎现在多少个psi", "工程师", custom.RecognitionPhrases).Text == null);
        });
        Test("spoken thresholds retain decimals, units, equality and freshness", () =>
        {
            var q = new TelemetryQueries();
            var frame = SyntheticAdapter.Create(1, 0, now);
            frame.Fields["pressure.FL"] = frame.Fields["pressure.FL"] with { Value = 26.5 };
            foreach (var threshold in new[] { "26.5", "二十六点五" })
                Check(q.Answer("左前胎压有没有超过" + threshold + "psi", frame, now).Text.Contains("左前轮未高于26.5"));
            Check(q.Answer("左前胎压低于27psi吗", frame, now).Text.Contains("左前轮低于27"));
            var imperial = DisplayUnits.ForDisplay(frame, true);
            Check(q.Answer("左前胎温超过60摄氏度吗", imperial, now).Text.Contains("左前轮高于60 °C"));
            Check(!q.Answer("左前胎压低于27摄氏度吗", frame, now).HasVerifiedData);
            Check(!q.Answer("左前胎压低于27psi吗", frame, now.AddMinutes(1)).HasVerifiedData);
        });
        Test("silence refuses", () => Check(VoiceCommandPolicy.Select([]).Text is null));
        Test("low confidence refuses", () => Check(VoiceCommandPolicy.Select([("油量", .4f)]).Text is null));
        Test("nonfinite confidence refuses", () => Check(VoiceCommandPolicy.Select([("油量", float.NaN)]).Text is null));
        Test("multiple utterances refuse", () => Check(VoiceCommandPolicy.Select([("油量", .9f), ("胎压", .9f)]).Text is null));
        Test("negated command refuses", () => Check(VoiceCommandPolicy.Select([("不要告诉我油量", .99f)]).Text is null));
        Test("unrelated command refuses", () => Check(VoiceCommandPolicy.Select([("打开音乐", .99f)]).Text is null));
        Test("custom confidence and split wake dictation", () =>
        {
            Check(VoiceCommandPolicy.Select([("轮胎温度多少", .44f)], "领队", .35f).Text == "轮胎温度多少");
            var result = VoiceCommandPolicy.Select([("领 队", .9f), ("我应该第几圈进站", .8f)], "领队", .35f);
            Check(AiRadio.AfterWakeWord(result.Text!, "领队") == "我应该第几圈进站");
            Check(AiRadio.AfterWakeWord("不要呼叫领队", "领队") is null);
            Check(AiRadio.AfterWakeWord("工程师，进站吗", "领队") is null);
        });
        cases.Add(("AI / compatible request, limited history and fresh telemetry only", async () =>
        {
            var frame = SyntheticAdapter.Create(1, 0, DateTimeOffset.UtcNow);
            frame.Fields["fuel"] = frame.Fields["fuel"] with { Quality = DataQuality.Stale };
            using var handler = new ReplyHandler();
            using var client = new HttpClient(handler);
            var reply = await AiRadio.AskAsync(AiRadio.Endpoint("http://localhost:1234/v1"), "radio-model", "test-only", "进站吗",
                frame, [new RadioMessage("user", "上个问题")], CancellationToken.None, client);
            Check(reply == "数据不足。");
            Check(handler.Path == "/v1/chat/completions" && handler.Key == "test-only");
            using var payload = System.Text.Json.JsonDocument.Parse(handler.Body!);
            var messages = payload.RootElement.GetProperty("messages");
            var input = messages[2].GetProperty("content").GetString()!;
            Check(input.Contains("Synthetic") && input.Contains("pressure.FL") && !input.Contains("\"fuel\"") && input.Contains("进站吗"));
        }));
        Test("all accepted phrases have query intent", () =>
        {
            var s = SyntheticAdapter.Create(1, 0, now);
            foreach (var field in RadioVocabulary.Fields)
                s.Fields.TryAdd(field.Key, new Measurement(1, "", now, DataQuality.Valid, "fixture", "test"));
            var queries = new TelemetryQueries();
            s = s with { Broadcast = new BroadcastFrame("测试", true, BroadcastRadio.Fields.ToDictionary(f => f.Key,
                f => new Measurement(null, "", now, DataQuality.Valid, "UDP fixture", "4") { TextValue = f.Title })) };
            foreach (var phrase in TelemetryQueries.SpokenPhrases) Check(queries.Answer(phrase, s, now).HasVerifiedData);
        });
        Test("custom vocabulary replaces the selected field across speech and text", () =>
        {
            var s = SyntheticAdapter.Create(1, 0, now);
            var queries = new TelemetryQueries(new Dictionary<string, string[]> { ["airTemperature"] = ["报告气温"] }, "工程师");
            Check(!queries.Answer("气温多少", s, now).HasVerifiedData);
            Check(queries.Answer("报告气温", s, now).EvidenceFields.SequenceEqual(["airTemperature"]));
            Check(VoiceCommandPolicy.Select([("报告 气温", .9f)], vocabulary: queries.RecognitionPhrases).Text == "报告气温");
            Check(VoiceCommandPolicy.Select([("气温多少", .9f)], vocabulary: queries.RecognitionPhrases).Text is null);
            Check(queries.Answer("油量", s, now).HasVerifiedData);
        });
        Test("every configurable telemetry field has multiple spoken alternatives", () =>
        {
            var queries = new TelemetryQueries();
            foreach (var field in RadioVocabulary.Fields) Check(queries.FieldPhrases(field.Key).Length >= 6);
            var s = SyntheticAdapter.Create(1, 0, now);
            Check(queries.Answer("右后胎几度", s, now).EvidenceFields.SequenceEqual(["temperature.RR"]));
            Check(queries.Answer("我领先后车多少秒", s, now).EvidenceFields.SequenceEqual(["gapBehind"]));
        });
        Test("source and units survive speech formatting", () =>
        {
            var text = WindowsSpeech.ForSpeech("【演示数据】剩余燃油 45 L，胎压 26.5 psi。");
            Check(text.StartsWith("演示数据") && text.Contains("升") && text.Contains("磅每平方英寸"));
        });
        Test("race summary cannot validate stale fields", () =>
        {
            var s = SyntheticAdapter.Create(1, 0, now);
            s.Fields["position"] = s.Fields["position"] with { Quality = DataQuality.Stale };
            var reply = new TelemetryQueries().Answer("赛场状况", s, now);
            Check(!reply.HasVerifiedData && !reply.EvidenceFields.Contains("position") && reply.Text.Contains("不能可靠回答"));
        });
        Test("remaining time respects calibrated seconds", () =>
        {
            var s = SyntheticAdapter.Create(1, 0, now);
            s.Fields["sessionTimeLeft"] = s.Fields["sessionTimeLeft"] with { Value = 120, Unit = "s" };
            Check(new TelemetryQueries().Answer("还剩多少时间", s, now).Text.Contains("2.0 分钟"));
        });
    }

    private sealed class ReplyHandler : HttpMessageHandler
    {
        public string? Body, Key, Path;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Body = await request.Content!.ReadAsStringAsync(token);
            Key = request.Headers.Authorization?.Parameter;
            Path = request.RequestUri?.AbsolutePath;
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) {
                Content = new StringContent("""{"choices":[{"message":{"content":"数据不足。"}}]}""")
            };
        }
    }
}
