namespace DeltaTR.Hardware.Moza;

public enum ButtonGesture { None, Tap, HoldStart, HoldEnd, Cancel }

/// <summary>One physical press produces either a tap or a hold, never both.</summary>
public sealed class ScreenButtonGesture
{
    private bool? previous;
    private bool armed, holding;
    private TimeSpan began;
    public ButtonGesture Update(bool? down, TimeSpan now)
    {
        var before = previous; previous = down;
        if (!down.HasValue)
        {
            bool cancel = holding; armed = holding = false;
            return cancel ? ButtonGesture.Cancel : ButtonGesture.None;
        }
        if (!before.HasValue) return ButtonGesture.None; // Attach with button held: require release first.
        if (down.Value && !before.Value) { armed = true; began = now; }
        if (!armed) return ButtonGesture.None;
        if (down.Value && !holding && now - began >= TimeSpan.FromMilliseconds(500))
        { holding = true; return ButtonGesture.HoldStart; }
        if (!down.Value)
        {
            bool wasHolding = holding; armed = holding = false;
            return wasHolding ? ButtonGesture.HoldEnd : now - began < TimeSpan.FromMilliseconds(500) ? ButtonGesture.Tap : ButtonGesture.None;
        }
        return ButtonGesture.None;
    }
}

/// <summary>Manual presentation persists; transient alerts return only after their display lifetime.</summary>
public sealed class ScreenPresentation
{
    public bool Pinned { get; private set; }
    private long dismissed = -1;
    public void Reset() { Pinned = false; dismissed = -1; }
    public bool Show(bool automatic, bool active, long turn) => Pinned || automatic && active && dismissed != turn;
    public void Toggle(bool visible, long turn)
    {
        Pinned = !visible;
        if (!Pinned) dismissed = turn;
    }
    public void BeginVoice() => Pinned = true;
}
