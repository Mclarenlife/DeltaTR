using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using DeltaTR.Core;

namespace DeltaTR.Storage;

/// <summary>Bounded, non-blocking recorder. Disk failures propagate to the caller via Completion/Dispose.</summary>
public sealed class JsonlRecorder : IAsyncDisposable
{
    private readonly Channel<RaceSnapshot> queue;
    private long dropped;
    public long DroppedFrames => Interlocked.Read(ref dropped);
    public Task Completion { get; }
    public string Path { get; }

    public JsonlRecorder(string path, int capacity = 512)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        // Never silently overwrite a recording. File creation is synchronous so UI can report failure immediately.
        var stream = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, true);
        queue = Channel.CreateBounded<RaceSnapshot>(new BoundedChannelOptions(capacity)
        { FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = false });
        Completion = DrainAsync(stream);
    }

    public bool TryWrite(RaceSnapshot snapshot)
    {
        if (!Completion.IsCompleted && queue.Writer.TryWrite(snapshot)) return true;
        Interlocked.Increment(ref dropped);
        return false;
    }

    private async Task DrainAsync(FileStream stream)
    {
        try
        {
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            var count = 0;
            await foreach (var snapshot in queue.Reader.ReadAllAsync())
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(snapshot, TelemetryJson.Options));
                if (++count % 10 == 0) await writer.FlushAsync();
            }
            await writer.FlushAsync();
        }
        finally { queue.Writer.TryComplete(); }
    }

    public async ValueTask DisposeAsync()
    {
        queue.Writer.TryComplete();
        await Completion.ConfigureAwait(false);
    }
}

public static class JsonlRecording
{
    public static async IAsyncEnumerable<RaceSnapshot> ReadAsync(string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        string? line;
        var number = 0;
        long previous = -1;
        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            number++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            RaceSnapshot snapshot;
            try
            {
                snapshot = JsonSerializer.Deserialize<RaceSnapshot>(line, TelemetryJson.Options)
                    ?? throw new JsonException("空快照");
                if (snapshot.SchemaVersion != RaceSnapshot.CurrentSchema || snapshot.Fields is null ||
                    snapshot.ElapsedMilliseconds < 0 || snapshot.ElapsedMilliseconds < previous ||
                    snapshot.CapturedAt == default || snapshot.SessionId is null ||
                    !Enum.IsDefined(snapshot.Phase) || !Enum.IsDefined(snapshot.Origin) ||
                    snapshot.Fields.Any(x => x.Value is null || !Enum.IsDefined(x.Value.Quality) ||
                        x.Value.Unit is null || x.Value.Source is null || x.Value.GameVersion is null))
                    throw new JsonException("版本、时间顺序或字段元数据无效");
            }
            catch (JsonException error)
            {
                throw new InvalidDataException($"录制文件第 {number} 行损坏：{error.Message}", error);
            }
            previous = snapshot.ElapsedMilliseconds;
            yield return snapshot;
        }
    }

    public static async IAsyncEnumerable<RaceSnapshot> ReplayAsync(string path, double speed = 1,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!double.IsFinite(speed) || speed <= 0 || speed > 100) throw new ArgumentOutOfRangeException(nameof(speed));
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long? first = null;
        await foreach (var snapshot in ReadAsync(path, cancellationToken))
        {
            first ??= snapshot.ElapsedMilliseconds;
            var due = (snapshot.ElapsedMilliseconds - first.Value) / speed;
            var wait = due - clock.Elapsed.TotalMilliseconds;
            while (wait > 0)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(wait, 1000)), cancellationToken);
                wait = due - clock.Elapsed.TotalMilliseconds;
            }
            yield return snapshot; // Preserve original timestamps/quality. Callers use CapturedAt as replay clock.
        }
    }

    public static async Task ExportCsvAsync(string input, string output, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        await writer.WriteLineAsync("capturedAt,elapsedMs,sessionId,origin,phase,metric,value,unit,quality,source,observedAt,gameVersion,isEstimated");
        await foreach (var snapshot in ReadAsync(input, cancellationToken))
        foreach (var (name, value) in snapshot.Fields)
        {
            var cells = new[] { snapshot.CapturedAt.ToString("O"), snapshot.ElapsedMilliseconds.ToString(), snapshot.SessionId,
                snapshot.Origin.ToString(), snapshot.Phase.ToString(), name,
                value.Value?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                value.Unit, value.Quality.ToString(), value.Source, value.ObservedAt.ToString("O"), value.GameVersion, value.IsEstimated.ToString() };
            await writer.WriteLineAsync(string.Join(',', cells.Select(EscapeCsv)));
        }
    }

    private static string EscapeCsv(string value)
    {
        // Avoid executing spreadsheet formulas when opening diagnostic exports.
        if (value.Length > 0 && "=+@\t\r".Contains(value[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
