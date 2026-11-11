using DeltaTR.Core;

namespace DeltaTR.Telemetry.Abstractions;

public interface IGameAdapter : IDisposable
{
    RaceSnapshot Read();
}

/// <summary>Atomic publication; UI/queries consume the latest frame without blocking sampling.</summary>
public sealed class StateStore
{
    private RaceSnapshot? latest;
    public RaceSnapshot? Latest => Volatile.Read(ref latest);
    public void Publish(RaceSnapshot snapshot) => Interlocked.Exchange(ref latest, snapshot);
    public void Clear() => Interlocked.Exchange(ref latest, null);
}
