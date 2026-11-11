using System.Runtime.InteropServices;
using NAudio.Wave;

namespace DeltaTR.Voice;

/// <summary>A single bounded PTT utterance, in RAM only. Stop closes the microphone before recognition.</summary>
public sealed class MicrophoneCapture : IDisposable
{
    private readonly WaveInEvent input = new() { DeviceNumber = -1, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 50 };
    private readonly MemoryStream buffer = new();
    private readonly TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object sync = new();
    private bool stopping, disposed, processedAudio;
    private SpeechInputProcessor? processor;

    public void SetProcessing(bool enhance, bool denoise)
    {
        lock (sync) processor = enhance || denoise ? new SpeechInputProcessor(enhance, denoise) : null;
    }
    public event Action<float[]>? SamplesAvailable;
    public event Action<double>? LevelChanged;
    public event Action<Exception?>? MonitorEnded;

    public double PeakPercent { get; private set; }
    public int CapturedMilliseconds => (int)(buffer.Length / 32);

    public MicrophoneCapture(int deviceNumber = -1, bool levelOnly = false)
    {
        input.DeviceNumber = deviceNumber;
        input.DataAvailable += (_, e) =>
        {
            int peak = 0;
            lock (sync)
            {
                if (disposed) return;
                // At most 16 seconds at 16 kHz mono PCM, even if the UI timer stalls.
                var length = Math.Min(e.BytesRecorded, 512000 - (int)buffer.Length);
                var samples = MemoryMarshal.Cast<byte, short>(e.Buffer.AsSpan(0, length));
                if (processor is not null)
                {
                    processor.Process(samples);
                    processedAudio = true;
                }
                foreach (var sample in samples) peak = Math.Max(peak, Math.Abs((int)sample));
                if (!levelOnly && length > 0)
                {
                    buffer.Write(e.Buffer, 0, length);
                    if (SamplesAvailable is { } accept)
                    {
                        var chunk = new float[samples.Length];
                        for (var i = 0; i < samples.Length; i++) chunk[i] = samples[i] / 32768f;
                        accept(chunk);
                    }
                }
            }
            var level = Math.Min(100, peak * 100d / 32768);
            PeakPercent = Math.Max(PeakPercent, level);
            LevelChanged?.Invoke(level);
        };
        input.RecordingStopped += (_, e) =>
        {
            if (levelOnly) MonitorEnded?.Invoke(e.Exception);
            else if (e.Exception is null) ended.TrySetResult();
            else ended.TrySetException(e.Exception);
        };
    }

    public static bool MeetsInputThreshold(double peakPercent, int threshold) =>
        peakPercent > 0 && peakPercent <= 100 && peakPercent >= threshold;

    public bool HasSustainedInput(int threshold) =>
        HasSustainedInput(MemoryMarshal.Cast<byte, short>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length)), threshold);

    internal static bool HasSustainedInput(ReadOnlySpan<short> samples, int threshold)
    {
        // A PTT click can pass the peak gate on its own. Look for just 30 ms of
        // contiguous energy, well below the user's peak threshold, before decoding.
        const int window = 160; // 10 ms at 16 kHz
        double floor = Math.Max(12, 32768 * threshold / 100d * 0.1);
        int consecutive = 0;
        for (int i = 0; i + window <= samples.Length; i += window)
        {
            double energy = 0;
            foreach (var sample in samples.Slice(i, window)) energy += (double)sample * sample;
            consecutive = energy >= floor * floor * window ? consecutive + 1 : 0;
            if (consecutive >= 3) return true;
        }
        return false;
    }

    public void Start() => input.StartRecording();
    public async Task<byte[]> StopAsync()
    {
        if (!stopping) { stopping = true; input.StopRecording(); }
        await ended.Task.WaitAsync(TimeSpan.FromSeconds(3));
        return PrepareWave(buffer.ToArray(), PeakPercent, normalizeQuietInput: !processedAudio);
    }

    /// <summary>Prepare a captured 16-bit mono 16 kHz utterance for the offline recognizer.</summary>
    public static byte[] PrepareWave(byte[] pcm, double peakPercent, bool normalizeQuietInput = true)
    {
        using var wave = new MemoryStream();
        using (var writer = new WaveFileWriter(wave, new WaveFormat(16000, 16, 1)))
        {
            // Boost quiet speech modestly, after measuring the unmodified microphone level.
            var gain = normalizeQuietInput ? Math.Clamp(60d / Math.Max(1, peakPercent), 1, 4) : 1;
            for (var i = 0; i + 1 < pcm.Length; i += 2)
            {
                var sample = (short)Math.Clamp(BitConverter.ToInt16(pcm, i) * gain, short.MinValue, short.MaxValue);
                BitConverter.TryWriteBytes(pcm.AsSpan(i, 2), sample);
            }
            writer.Write(pcm, 0, pcm.Length);
            writer.Write(new byte[16000], 0, 16000); // 500 ms of endpoint silence after PTT release.
        }
        return wave.ToArray();
    }

    public void Dispose()
    {
        lock (sync) { if (disposed) return; disposed = true; }
        input.Dispose();
        lock (sync) buffer.Dispose();
    }
}
