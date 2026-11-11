using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DeltaTR.App.ViewModels;
using DeltaTR.Hardware.Moza;

namespace DeltaTR.App.Input;

internal enum TalkEdge { None, Press, Release, Lost }

internal sealed class HoldButton
{
    private bool? previous;
    public TalkEdge Update(bool? down)
    {
        var before = previous;
        previous = down;
        if (!before.HasValue || before == down) return TalkEdge.None;
        if (!down.HasValue) return before.Value ? TalkEdge.Lost : TalkEdge.None;
        return down.Value ? TalkEdge.Press : TalkEdge.Release;
    }
}

internal sealed class PushToTalk : IDisposable
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly MainViewModel vm;
    private readonly WheelButtons wheels = new();
    private readonly HoldButton hold = new();
    private IReadOnlyList<WheelState> previousWheels = [];
    private int bindingRevision = -1;
    private int? ownedVersion;
    private readonly ScreenButtonGesture gesture = new();
    private readonly System.Diagnostics.Stopwatch gestureClock = System.Diagnostics.Stopwatch.StartNew();
    private bool smartMode;
    public PushToTalk(MainViewModel viewModel)
    {
        vm = viewModel;
        timer.Tick += Poll;
        timer.Start();
    }

    internal static TalkBinding? FindPressedButton(IReadOnlyList<WheelState> before, IReadOnlyList<WheelState> current)
    {
        foreach (var wheel in current)
        {
            var prior = before.FirstOrDefault(w => w.Id == wheel.Id);
            if (prior is null) continue;
            for (var i = 0; i < Math.Min(prior.Buttons.Length, wheel.Buttons.Length); i++)
                if (wheel.Buttons[i] && !prior.Buttons[i]) return new(wheel.Id, wheel.Name, i);
        }
        return null;
    }

    private async void Poll(object? sender, EventArgs e)
    {
        if (!vm.HasGame) { ownedVersion = null; hold.Update(null); gesture.Update(null, gestureClock.Elapsed); return; }
        if (Application.Current.MainWindow is not { } window) return;
        var binding = vm.SelectedTalkKey;
        if (bindingRevision != vm.BindingRevision)
        {
            bindingRevision = vm.BindingRevision;
            previousWheels = [];
            ownedVersion = null;
            hold.Update(null);
            gesture.Update(null, gestureClock.Elapsed);
            if (!binding.DeviceId.HasValue && !vm.BindingInput) wheels.Dispose();
        }
        IReadOnlyList<WheelState> states = vm.BindingInput || binding.DeviceId.HasValue
            ? wheels.Read(new WindowInteropHelper(window).Handle) : [];
        if (vm.BindingInput)
        {
            if (!window.IsActive) vm.SetBindingMode(false);
            else if (FindPressedButton(previousWheels, states) is { } pressed) vm.AcceptBinding(pressed);
            else vm.SetBindingHelp("按一下键盘键或方向盘按钮，Esc 取消。  " + wheels.Status);
            previousWheels = states;
            return;
        }

        bool? down;
        if (binding.DeviceId is { } id)
        {
            var wheel = states.FirstOrDefault(w => w.Id == id);
            down = wheel is not null && binding.Code < wheel.Buttons.Length ? wheel.Buttons[binding.Code] : null;
        }
        else down = (GetAsyncKeyState(binding.Code) & 0x8000) != 0;
        var edge = hold.Update(down);
        var editing = !binding.DeviceId.HasValue && binding.Code is not (>= 0x70 and <= 0x87) && window.IsActive &&
            Keyboard.FocusedElement is TextBoxBase or PasswordBox;
        if (smartMode != vm.UseSmartWheelButton)
        {
            smartMode = vm.UseSmartWheelButton;
            if (ownedVersion == vm.CaptureVersion) vm.CancelVoice();
            ownedVersion = null; gesture.Update(null, gestureClock.Elapsed); hold.Update(null);
            return;
        }
        if (smartMode)
        {
            var action = gesture.Update(editing || !window.IsEnabled ? null : down, gestureClock.Elapsed);
            if (action == ButtonGesture.Tap) vm.ToggleWheelPresentation();
            else if (action == ButtonGesture.HoldStart && !vm.IsListening && vm.CanTalk)
            {
                vm.BeginWheelVoice(); vm.BeginTalk();
                ownedVersion = vm.IsListening ? vm.CaptureVersion : null;
            }
            else if (action is ButtonGesture.HoldEnd or ButtonGesture.Cancel)
            {
                bool owns = ownedVersion == vm.CaptureVersion; ownedVersion = null;
                if (owns)
                {
                    if (action == ButtonGesture.Cancel) vm.CancelVoice();
                    else await vm.EndTalkAsync();
                }
            }
            return;
        }
        if (edge == TalkEdge.Press && !editing && !vm.IsListening && vm.CanTalk && window.IsEnabled)
        {
            vm.BeginTalk();
            ownedVersion = vm.IsListening ? vm.CaptureVersion : null;
        }
        if (edge is TalkEdge.Release or TalkEdge.Lost)
        {
            var ownsCapture = ownedVersion == vm.CaptureVersion;
            ownedVersion = null;
            if (!ownsCapture) return;
            if (edge == TalkEdge.Lost) vm.CancelVoice();
            else await vm.EndTalkAsync();
        }
    }
    public void Dispose()
    {
        timer.Stop();
        timer.Tick -= Poll;
        wheels.Dispose();
        vm.CancelVoice();
    }
}
