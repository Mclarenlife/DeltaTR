namespace DeltaTR.Voice;

/// <summary>Streaming 16 kHz mono PCM processing. Each capture owns its filter history.</summary>
public sealed class SpeechInputProcessor(bool enhance, bool denoise)
{
    private const int BlockSize = 160; // 10 ms
    private readonly NoiseBand[] bands = [new(), new(), new()];
    private readonly double[,] components = new double[3, BlockSize];
    private double rumble, low, middle, voiceGain = 1;

    public void Process(Span<short> samples)
    {
        if (!enhance && !denoise) return;
        while (!samples.IsEmpty)
        {
            var block = samples[..Math.Min(BlockSize, samples.Length)];
            if (denoise) ReduceNoise(block);
            if (enhance) EnhanceVoice(block);
            samples = samples[block.Length..];
        }
    }

    private void ReduceNoise(Span<short> block)
    {
        for (var i = 0; i < block.Length; i++)
        {
            // One-pole crossovers at 85, 450 and 2200 Hz. The bands sum to the high-passed signal.
            rumble += 0.03283 * (block[i] - rumble);
            var signal = block[i] - rumble;
            low += 0.16198 * (signal - low);
            middle += 0.57849 * (signal - middle);
            components[0, i] = low;
            components[1, i] = middle - low;
            components[2, i] = signal - middle;
        }
        for (var index = 0; index < bands.Length; index++)
        {
            double energy = 0;
            for (var i = 0; i < block.Length; i++)
                energy += components[index, i] * components[index, i];
            bands[index].Update(energy / block.Length, block.Length);
        }
        for (var i = 0; i < block.Length; i++)
        {
            var sample = components[0, i] * bands[0].NextGain() + components[1, i] * bands[1].NextGain()
                + components[2, i] * bands[2].NextGain();
            block[i] = ToPcm(sample);
        }
    }

    private void EnhanceVoice(Span<short> block)
    {
        double energy = 0;
        int peak = 0;
        foreach (var sample in block)
        {
            energy += (double)sample * sample;
            peak = Math.Max(peak, Math.Abs((int)sample));
        }
        var rms = Math.Sqrt(energy / block.Length);
        // Fade in the boost above the quiet floor, and never boost a loud peak past headroom.
        var target = 1 + Math.Clamp((rms - 50) / 150, 0, 1) * (Math.Clamp(3500 / Math.Max(1, rms), 1, 3) - 1);
        var ceiling = Math.Max(1, 30000d / Math.Max(1, peak));
        voiceGain = Math.Min(voiceGain, ceiling);
        target = Math.Min(target, ceiling);
        var next = voiceGain + (target - voiceGain) * (1 - Math.Exp(-block.Length / 640d));
        var step = (next - voiceGain) / block.Length;
        for (var i = 0; i < block.Length; i++)
        {
            voiceGain += step;
            block[i] = ToPcm(block[i] * voiceGain);
        }
    }

    private static short ToPcm(double sample) => (short)Math.Clamp(Math.Round(sample), short.MinValue, short.MaxValue);

    private sealed class NoiseBand
    {
        private double floor = 1600, minimum = double.MaxValue, gain = 1, step;
        private int windowSamples;

        public void Update(double energy, int length)
        {
            // A half-second minimum follows persistent noise while ignoring brief speech peaks.
            minimum = Math.Min(minimum, energy);
            windowSamples += length;
            if (windowSamples >= 8000)
            {
                floor = Math.Max(64, 0.35 * floor + 0.65 * minimum);
                minimum = double.MaxValue;
                windowSamples = 0;
            }
            var target = Math.Clamp(1 - 1.6 * floor / Math.Max(1, energy), 0.18, 1);
            var time = target > gain ? 80d : 480d;
            step = (target - gain) * (1 - Math.Exp(-length / time)) / length;
        }
        public double NextGain() => gain += step;
    }
}
