using System.Windows.Input;

namespace DeltaTR.App.Input;

// A null device ID denotes the Windows keyboard. Controller codes use zero-based DirectInput button indices.
public sealed record TalkBinding(Guid? DeviceId, string DeviceName, int Code)
{
    public static TalkBinding Keyboard(int code) => new(null, "键盘", code);
    public bool IsValid => DeviceId.HasValue ? DeviceId != Guid.Empty && Code is >= 0 and < 128 : Code is >= 8 and <= 254 && Code != 27;
    public string Name => DeviceId.HasValue ? $"按钮 {Code + 1}" : KeyInterop.KeyFromVirtualKey(Code).ToString();
    public override string ToString() => $"{DeviceName} · {Name}";
}
