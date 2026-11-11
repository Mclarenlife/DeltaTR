using System.Globalization;
using System.Text;
using DeltaTR.Core;
using DeltaTR.Engineer;
using DeltaTR.Storage;
using DeltaTR.Telemetry.Abstractions;
using DeltaTR.Telemetry.ACC;

Console.OutputEncoding = Encoding.UTF8;
using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
try
{
    var options = new Dictionary<string, string?>();
    var flags = new HashSet<string> { "--help", "--demo", "--analyze" };
    var valued = new HashSet<string> { "--record", "--replay", "--csv", "--report", "--calibration", "--duration", "--hz", "--speed", "--query" };
    for (var i = 0; i < args.Length; i++)
    {
        var key = args[i];
        if (options.ContainsKey(key)) throw new ArgumentException($"重复参数：{key}");
        if (flags.Contains(key)) options[key] = null;
        else if (valued.Contains(key) && i + 1 < args.Length && !args[i + 1].StartsWith("--")) options[key] = args[++i];
        else throw new ArgumentException($"未知参数或缺少参数值：{key}");
    }
    if (options.ContainsKey("--help"))
    {
        Console.WriteLine("DeltaTR ACC Telemetry Probe 0.1.1\n" +
            "默认读取真实 ACC；Ctrl+C 安全结束。\n" +
            "--demo                  使用明确标注的合成数据\n" +
            "--record <file.jsonl>    录制，不覆盖已有文件\n" +
            "--replay <file.jsonl>    按原始单调时间回放\n" +
            "--csv <file.csv>         与 --replay 合用，导出长表\n" +
            "--analyze               与 --replay 合用，分析整段录制质量\n" +
            "--report <file.json>     保存 --analyze 质量报告，不覆盖已有文件\n" +
            "--calibration <json>     加载 HUD 核验记录\n" +
            "--duration <seconds>    限制运行时长（默认不限）\n" +
            "--hz <1..50>            采样频率，默认 20；终端每秒显示\n" +
            "--speed <0.1..100>       回放倍速，默认 1\n" +
            "--query <question>      对每次显示的快照进行文本诊断查询");
        return 0;
    }
    string? Get(string name) => options.GetValueOrDefault(name);
    double Numeric(string name, double fallback, double min, double max)
    {
        if (Get(name) is not { } text) return fallback;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{name} 必须在 {min} 到 {max} 之间。");
        return value;
    }
    if (options.ContainsKey("--demo") && options.ContainsKey("--replay")) throw new ArgumentException("--demo 与 --replay 不能同时使用。");
    if (options.ContainsKey("--record") && options.ContainsKey("--replay")) throw new ArgumentException("回放不能同时录制。");
    if (options.ContainsKey("--csv") && !options.ContainsKey("--replay")) throw new ArgumentException("--csv 需要 --replay 输入。");
    if (options.ContainsKey("--analyze") && (!options.ContainsKey("--replay") || options.ContainsKey("--csv") || options.ContainsKey("--query")))
        throw new ArgumentException("--analyze 需要 --replay，不能同时使用 --csv 或 --query。");
    if (options.ContainsKey("--report") && !options.ContainsKey("--analyze")) throw new ArgumentException("--report 需要 --analyze。");
    var hz = Numeric("--hz", 20, 1, 50);
    var speed = Numeric("--speed", 1, 0.1, 100);
    var duration = Numeric("--duration", 0, 0.1, 86400);
    if (duration > 0) stop.CancelAfter(TimeSpan.FromSeconds(duration));
    var queries = new TelemetryQueries();
    void Show(RaceSnapshot snapshot, bool replay)
    {
        Console.WriteLine($"\n{snapshot.CapturedAt:O} [{(replay ? "FILE REPLAY / " : "")}{snapshot.Origin}/{snapshot.Phase}] {snapshot.StatusMessage}");
        Console.WriteLine($"  {snapshot.CarModel} @ {snapshot.Track} | game={snapshot.GameVersion} sm={snapshot.SharedMemoryVersion} | p={snapshot.PhysicsPacketId} g={snapshot.GraphicsPacketId}");
        foreach (var (name, v) in snapshot.Fields)
            Console.WriteLine($"  {name,-24} {v.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "—",10} {v.Unit,-14} [{v.Quality}] {v.ObservedAt:HH:mm:ss.fff} {v.Source}");
        if (Get("--query") is { } question) Console.WriteLine("  TR: " + queries.Answer(question, snapshot, replay ? snapshot.CapturedAt : DateTimeOffset.UtcNow).Text);
    }
    if (Get("--replay") is { } input)
    {
        if (options.ContainsKey("--analyze"))
        {
            var report = await RecordingAnalyzer.AnalyzeAsync(input, stop.Token);
            Console.WriteLine(report.Summary);
            foreach (var (key, f) in report.Fields)
                Console.WriteLine($"  {key}: 新鲜解码 {f.FreshDecodedPercent:0.00}% / 已校准新鲜 {f.VerifiedFreshFrames} / 缺失 {f.MissingFrames}");
            if (Get("--report") is { } reportPath) await RecordingAnalyzer.WriteReportAsync(report, reportPath, stop.Token);
        }
        else if (Get("--csv") is { } output) await JsonlRecording.ExportCsvAsync(input, output, stop.Token);
        else await foreach (var snapshot in JsonlRecording.ReplayAsync(input, speed, stop.Token)) Show(snapshot, true);
        return 0;
    }
    using IGameAdapter adapter = options.ContainsKey("--demo") ? new SyntheticAdapter() : new AccAdapter(CalibrationProfile.Load(Get("--calibration")));
    await using var recorder = Get("--record") is { } path ? new JsonlRecorder(path) : null;
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1 / hz));
    long lastDisplayed = -1000;
    do
    {
        var snapshot = adapter.Read();
        if (recorder is not null)
        {
            recorder.TryWrite(snapshot);
            if (recorder.Completion.IsFaulted) await recorder.Completion;
        }
        if (snapshot.ElapsedMilliseconds - lastDisplayed >= 1000)
        {
            Show(snapshot, false); lastDisplayed = snapshot.ElapsedMilliseconds;
            if (recorder is not null) Console.WriteLine($"  录制丢帧：{recorder.DroppedFrames}");
        }
    } while (await timer.WaitForNextTickAsync(stop.Token));
    return 0;
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { Console.WriteLine("已停止，录制已刷新。"); return 0; }
catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or System.Text.Json.JsonException)
{ Console.Error.WriteLine("错误：" + e.Message); return 1; }
