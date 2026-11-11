using System.Diagnostics;
using DeltaTR.Core;
using DeltaTR.Telemetry.Abstractions;
using DeltaTR.Telemetry.ACC.Mapping;
using DeltaTR.Telemetry.ACC.SharedMemory;

namespace DeltaTR.Telemetry.ACC;

public sealed class AccAdapter : IGameAdapter
{
    private readonly Broadcast.BroadcastClient broadcast = new(() => Broadcast.BroadcastConfig.Read());
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly AccNormalizer normalizer;
    private ReadOnlyPage? physics, graphics, info;
    private long nextConnect;
    private string status = "等待 ACC 启动并进入赛道";
    private int? processId;

    public AccAdapter(CalibrationProfile? calibration = null) => normalizer = new(calibration ?? new());

    public RaceSnapshot Read()
    {
        var now = DateTimeOffset.UtcNow;
        var shared = ReadShared(now, clock.ElapsedMilliseconds);
        var feed = broadcast.Poll(processId, shared, now);
        return shared with { Broadcast = feed, Traffic = feed.Traffic ?? shared.Traffic };
    }

    private RaceSnapshot ReadShared(DateTimeOffset now, long elapsed)
    {
        try
        {
            var processes = Process.GetProcessesByName("AC2-Win64-Shipping");
            int? currentId;
            try { currentId = processes.Length == 1 ? processes[0].Id : null; }
            finally { foreach (var process in processes) process.Dispose(); }
            if (!currentId.HasValue)
            {
                Disconnect(); processId = null;
                return RaceSnapshot.Empty(now, elapsed, SessionPhase.Disconnected, processes.Length == 0 ? "正在等待 ACC 游戏进程。请启动 Assetto Corsa Competizione，进入练习赛并点击驾驶。" : "检测到多个 ACC 游戏进程，请仅保留一个游戏会话。" );
            }
            if (processId != currentId) { Disconnect(); processId = currentId; nextConnect = 0; }
            if (physics is null)
            {
                if (elapsed < nextConnect) return RaceSnapshot.Empty(now, elapsed, SessionPhase.Connecting, status);
                nextConnect = elapsed + 1000;
                physics = new(@"Local\acpmf_physics", AccLayout.Physics.Size);
                graphics = new(@"Local\acpmf_graphics", AccLayout.Graphics.Size);
                info = new(@"Local\acpmf_static", AccLayout.Static.Size);
            }
            // Re-read static metadata every sample: protects track/car/session transitions.
            // Pages remain independently timestamped; no claim of a simultaneous game tick.
            var raw = AccDecoder.Decode(physics.Read(true), graphics!.Read(true), info!.Read(false));
            return normalizer.Normalize(raw, now, elapsed);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            status = error switch
            {
                FileNotFoundException => "已检测到 ACC，等待游戏创建遥测。请载入赛道并点击驾驶，将自动重试。",
                UnauthorizedAccessException => "已检测到 ACC，但读取权限不足。请用同一 Windows 用户和相同权限运行两者。",
                _ => $"ACC 遥测读取失败，将自动重试：{error.Message}"
            };
            Disconnect();
            return RaceSnapshot.Empty(now, elapsed, SessionPhase.Connecting, status);
        }
    }

    private void Disconnect()
    {
        physics?.Dispose(); graphics?.Dispose(); info?.Dispose();
        physics = graphics = info = null;
        normalizer.Reset();
    }

    public void Dispose() { broadcast.Dispose(); Disconnect(); }
}
