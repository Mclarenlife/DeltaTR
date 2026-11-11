using DeltaTR.Hardware.Moza;

internal static class ScreenInteractionTests
{
    public static void Gestures()
    {
        static void Equal(ButtonGesture expected, ButtonGesture actual) { if (actual != expected) throw new Exception($"Expected {expected}; got {actual}"); }
        var gesture = new ScreenButtonGesture();
        ButtonGesture Tick(bool? down, int ms) => gesture.Update(down, TimeSpan.FromMilliseconds(ms));
        Equal(ButtonGesture.None, Tick(true, 0)); Equal(ButtonGesture.None, Tick(false, 10));
        Tick(true, 20); Equal(ButtonGesture.Tap, Tick(false, 200)); Equal(ButtonGesture.None, Tick(false, 210));
        Tick(true, 300); Equal(ButtonGesture.None, Tick(true, 799)); Equal(ButtonGesture.HoldStart, Tick(true, 800));
        Equal(ButtonGesture.None, Tick(true, 1200)); Equal(ButtonGesture.HoldEnd, Tick(false, 1400));
        Tick(true, 2000); Equal(ButtonGesture.HoldStart, Tick(true, 2500)); Equal(ButtonGesture.Cancel, Tick(null, 2510));
        Equal(ButtonGesture.None, Tick(true, 2600)); Equal(ButtonGesture.None, Tick(false, 2610));
        Tick(true, 2700); Equal(ButtonGesture.None, Tick(false, 3500)); // A stalled poll cannot turn a long hold into a tap.
    }
    public static void Presentation()
    {
        static void Check(bool condition) { if (!condition) throw new Exception("Screen ownership policy failed"); }
        var state = new ScreenPresentation();
        Check(!state.Show(true, false, 0));
        Check(state.Show(true, true, 1)); // Automatic task appears temporarily.
        Check(!state.Show(true, false, 1));
        state.Toggle(false, 1); Check(state.Pinned && state.Show(false, false, 1));
        Check(state.Show(true, false, 2)); // Finishing an alert cannot hide a manually pinned page.
        state.Toggle(true, 2); Check(!state.Show(true, true, 2)); // Dismiss the current alert.
        Check(state.Show(true, true, 3)); // The next event can appear.
        state.BeginVoice(); Check(state.Show(false, false, 3));
        state.Reset(); Check(!state.Show(false, true, 4));
    }
}
