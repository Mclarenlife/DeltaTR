using System.Runtime.InteropServices;
using NAudio;
using NAudio.Wave;

namespace DeltaTR.Voice;

public sealed record AudioDevice(int Number, string Name)
{
    public static List<AudioDevice> List(bool input)
    {
        var devices = new List<AudioDevice> { new(-1, "跟随系统默认设备") };
        var count = input ? WaveInEvent.DeviceCount : WaveInterop.waveOutGetNumDevs();
        for (var i = 0; i < count; i++)
        {
            string name;
            if (input) name = WaveInEvent.GetCapabilities(i).ProductName;
            else
            {
                var caps = new WaveOutCapabilities();
                if (WaveInterop.waveOutGetDevCaps(i, out caps, Marshal.SizeOf<WaveOutCapabilities>()) != MmResult.NoError) continue;
                name = caps.ProductName;
            }
            devices.Add(new(i, name));
        }
        return devices;
    }
}
