using SharpGen.Runtime;
using Vortice.DirectInput;

namespace DeltaTR.App.Input;

internal sealed record WheelState(Guid Id, string Name, bool[] Buttons);

internal sealed class WheelButtons : IDisposable
{
    private IDirectInput8? input;
    private readonly Dictionary<Guid, (IDirectInputDevice8 Device, string Name)> devices = [];
    private long nextScan;
    public string Status { get; private set; } = "";

    public IReadOnlyList<WheelState> Read(nint window)
    {
        var states = new List<WheelState>();
        try
        {
            if (Environment.TickCount64 >= nextScan)
            {
                nextScan = Environment.TickCount64 + 1000;
                input ??= DInput.DirectInput8Create();
                var attached = input.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
                foreach (var id in devices.Keys.Except(attached.Select(x => x.InstanceGuid)).ToArray()) Remove(id);
                foreach (var info in attached)
                {
                    if (devices.ContainsKey(info.InstanceGuid)) continue;
                    if (input.CreateDevice(info.InstanceGuid, out var device).Failure || device is null) continue;
                    if (device.SetDataFormat<RawJoystickState>().Failure ||
                        device.SetCooperativeLevel(window, CooperativeLevel.Background | CooperativeLevel.NonExclusive).Failure)
                    {
                        device.Dispose();
                        continue;
                    }
                    devices.Add(info.InstanceGuid, (device, info.InstanceName));
                }
            }
            foreach (var (id, entry) in devices)
            {
                if (entry.Device.Acquire().Failure || entry.Device.Poll().Failure) continue;
                try { states.Add(new WheelState(id, entry.Name, entry.Device.GetCurrentJoystickState().Buttons)); }
                catch (SharpGenException) { /* Input was lost between polling and reading. Retry on the next tick. */ }
            }
            Status = states.Count > 0 ? string.Join("、", states.Select(s => s.Name)) :
                devices.Count > 0 ? "控制器暂不可读，请检查驱动或退出独占控制的程序" : "未检测到可读取的方向盘或手柄";
        }
        catch (Exception e) when (e is SharpGenException or DllNotFoundException or EntryPointNotFoundException)
        {
            Status = "方向盘读取失败：" + e.Message;
        }
        return states;
    }
    private void Remove(Guid id)
    {
        var device = devices[id].Device;
        devices.Remove(id);
        device.Unacquire();
        device.Dispose();
    }
    public void Dispose()
    {
        foreach (var id in devices.Keys.ToArray()) Remove(id);
        input?.Dispose();
        input = null;
        nextScan = 0;
    }
}
