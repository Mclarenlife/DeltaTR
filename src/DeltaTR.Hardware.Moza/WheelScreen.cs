using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using Microsoft.Win32;

namespace DeltaTR.Hardware.Moza;

public sealed record WheelPort(string Port, string Name);
public sealed record WheelMessage(string State, string Text, int Turn = 0);

/// <summary>Opt-in, exclusive display connection. Pit House is never stopped by this service.</summary>
public sealed class WheelScreen : IAsyncDisposable
{
    private CancellationTokenSource? stop;
    private Task? worker;
    private WheelMessage latest = new("idle", "");
    private string status = "未连接方向盘屏幕";
    public string Status => Volatile.Read(ref status);
    public bool IsRunning => worker is { IsCompleted: false };
    public void Update(WheelMessage value) => Volatile.Write(ref latest, value);
    public static IReadOnlyList<WheelPort> FindPorts()
    {
        var result = new List<WheelPort>();
        using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
        if (usb is null) return result;
        foreach (var id in usb.GetSubKeyNames().Where(n => n.StartsWith("VID_346E&PID_0000", StringComparison.OrdinalIgnoreCase)))
        {
            using var devices = usb.OpenSubKey(id);
            if (devices is null) continue;
            foreach (var name in devices.GetSubKeyNames())
            {
                using var device = devices.OpenSubKey(name);
                using var parameters = device?.OpenSubKey("Device Parameters");
                if (parameters?.GetValue("PortName") is string port && SerialPort.GetPortNames().Contains(port))
                    result.Add(new(port, "MOZA 基座 · " + port));
            }
        }
        return result.DistinctBy(p => p.Port).OrderBy(p => p.Port).ToArray();
    }
    public async Task StartAsync(string port)
    {
        await StopAsync();
        var pitHouse = Process.GetProcessesByName("MOZA Pit House");
        bool pitHouseRunning = pitHouse.Length > 0;
        foreach (var process in pitHouse) process.Dispose();
        if (pitHouseRunning)
            throw new InvalidOperationException("请先退出 Pit House，再开启方向盘屏显。两者暂时不能同时连接。");
        if (!FindPorts().Any(p => p.Port == port)) throw new InvalidOperationException("没有找到所选 MOZA 设备，请检查连接并刷新。");
        var cancellation = stop = new();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        status = "正在连接 KS Pro · 约需 15 秒";
        worker = Task.Run(() =>
        {
            using var serial = new SerialPort(port, 115200, Parity.None, 8, StopBits.One)
                { ReadTimeout = 30, WriteTimeout = 500, ReadBufferSize = 65536, DtrEnable = true };
            try
            {
                serial.Open();
                var connection = new MozaDisplaySession(bytes => serial.Write(bytes, 0, bytes.Length),
                    () => { try { return serial.ReadByte(); } catch (TimeoutException) { return -1; } },
                    line => { if (line.Contains("STRING CHANNEL READY", StringComparison.Ordinal))
                        { status = "KS Pro 已连接 · 独立屏显模式"; ready.TrySetResult(); } });
                var clock = Stopwatch.StartNew();
                WheelMessage? previous = null;
                TimeSpan pageStart = TimeSpan.Zero;
                connection.RunLive(() =>
                {
                    var current = Volatile.Read(ref latest);
                    if (current.State != previous?.State || current.Turn != previous?.Turn)
                        pageStart = clock.Elapsed;
                    previous = current;
                    return Encode(current, clock.Elapsed - pageStart);
                }, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            { status = "方向盘屏显已关闭"; ready.TrySetCanceled(cancellation.Token); }
            catch (Exception e)
            { status = "方向盘连接失败：" + e.Message; ready.TrySetException(e); }
            finally { if (!ready.Task.IsCompleted) ready.TrySetException(new IOException("显示连接已结束。")); }
        });
        try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(45)); }
        catch { await StopAsync(); throw; }
    }
    public async Task StopAsync()
    {
        stop?.Cancel();
        if (worker is not null) await worker;
        stop?.Dispose(); stop = null; worker = null;
    }
    public async ValueTask DisposeAsync() => await StopAsync();

    // A single string channel holds state and one UTF-8 page. No JSON escaping expansion.
    // Page changes every five seconds; the full response remains visible in the app.
    public static TimeSpan ReadingDuration(string text) => TimeSpan.FromSeconds(Math.Max(8, SplitPages(text).Count * 5 + 1));

    private static List<string> SplitPages(string text)
    {
        var pages = new List<string>(); var page = new StringBuilder(); int bytes = 0;
        foreach (var rune in text.EnumerateRunes().Take(4000))
        {
            int n = rune.Utf8SequenceLength;
            if (bytes + n > 105) { pages.Add(page.ToString()); page.Clear(); bytes = 0; }
            page.Append(rune); bytes += n;
        }
        pages.Add(page.ToString());
        return pages;
    }

    public static string Encode(WheelMessage message, TimeSpan elapsed)
    {
        var state = message.State is "listen" or "think" or "reply" or "offline" ? message.State : "idle";
        var pages = SplitPages(message.Text);
        int index = (int)(Math.Max(0, elapsed.TotalSeconds) / 5 % pages.Count);
        string marker = pages.Count > 1 ? $" [{index + 1}/{pages.Count}]" : "";
        return state + "|" + pages[index] + marker;
    }
}
