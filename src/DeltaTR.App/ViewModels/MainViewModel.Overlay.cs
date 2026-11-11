using System.Windows.Input;
using DeltaTR.App.Settings;
using DeltaTR.Core;

namespace DeltaTR.App.ViewModels;

public sealed record PlacementOption(string Key, string Label);

public sealed partial class MainViewModel
{
    private enum BubbleTurn { Idle, Typing, Listening, Reply, Alert }
    private BubbleTurn bubbleTurn;
    private string? overlaySession;
    private DateTimeOffset hideAfter;
    private static readonly TimeSpan ReadingTime = TimeSpan.FromSeconds(8);
    public bool BubbleVisible => overlaySession is null || bubbleTurn != BubbleTurn.Idle;
    public string OverlayStatus => overlaySession is null ? "" : bubbleTurn switch
    {
        BubbleTurn.Typing => "车手正在输入…",
        BubbleTurn.Reply => LastQuestion,
        BubbleTurn.Alert => "赛事提醒",
        _ => ""
    };
    public string BubbleText => overlaySession is null ? "未连接" : bubbleTurn switch
    {
        BubbleTurn.Typing => Question,
        BubbleTurn.Listening => VoiceStatus,
        BubbleTurn.Reply => Answer,
        BubbleTurn.Alert => LastRaceAlert,
        _ => ""
    };

    private void SetTyping(bool value)
    {
        if (value) SetBubbleTurn(BubbleTurn.Typing);
        else if (bubbleTurn == BubbleTurn.Typing) SetBubbleTurn(BubbleTurn.Idle);
    }

    private void SetBubbleTurn(BubbleTurn next)
    {
        if (next is BubbleTurn.Listening or BubbleTurn.Reply or BubbleTurn.Alert) screenTurn++;
        bubbleTurn = overlaySession is null ? BubbleTurn.Idle : next;
        hideAfter = DateTimeOffset.UtcNow + ReadingTime;
        if (IsAnyWheelScreenRunning && next is BubbleTurn.Reply or BubbleTurn.Alert)
            hideAfter = DateTimeOffset.UtcNow + DeltaTR.Hardware.Moza.WheelScreen.ReadingDuration(next == BubbleTurn.Reply ? Answer : LastRaceAlert);
        UpdateBubble();
    }

    private void UpdateOverlaySession()
    {
        var connected = !stopped && shown is not null && shown.Phase is
            SessionPhase.Idle or SessionPhase.Live or SessionPhase.Paused or SessionPhase.Replay &&
            DateTimeOffset.UtcNow - shown.CapturedAt <= Metrics.MaxAge;
        var session = connected ? shown!.SessionId :
            !stopped && shown?.Broadcast?.Driving == true ? "broadcast" : null;
        if (session != overlaySession)
        {
            overlaySession = session;
            SetBubbleTurn(BubbleTurn.Idle);
        }
        else if (bubbleTurn is BubbleTurn.Listening or BubbleTurn.Reply or BubbleTurn.Alert && !IsListening && !processing &&
            aiStop is null && !speech.IsSpeaking && DateTimeOffset.UtcNow >= hideAfter)
            SetBubbleTurn(BubbleTurn.Idle);
        UpdateWheelScreen();
    }

    private void UpdateBubble()
    {
        Changed(nameof(OverlayStatus)); Changed(nameof(BubbleText)); Changed(nameof(BubbleVisible));
    }

    private RadioOverlay? overlayWindow;
    public IReadOnlyList<PlacementOption> Placements { get; } =
        [new("Left", "文字在左"), new("Right", "文字在右"), new("Top", "文字在上"), new("Bottom", "文字在下")];
    private string textPlacement = "Right";
    public string TextPlacement
    {
        get => textPlacement;
        set { textPlacement = Placements.Any(x => x.Key == value) ? value : "Right"; Changed(); }
    }
    private int textOpacityPercent = 100;
    public int TextOpacityPercent
    {
        get => textOpacityPercent;
        set { textOpacityPercent = Math.Clamp(value, 0, 100); Changed(); Changed(nameof(TextAlpha)); }
    }
    public double TextAlpha => TextOpacityPercent / 100d;
    private int avatarOpacityPercent = 100;
    public int AvatarOpacityPercent
    {
        get => avatarOpacityPercent;
        set { avatarOpacityPercent = Math.Clamp(value, 0, 100); Changed(); Changed(nameof(AvatarAlpha)); }
    }
    public double AvatarAlpha => AvatarOpacityPercent / 100d;
    private int backdropOpacityPercent = 100;
    public int BackdropOpacityPercent
    {
        get => backdropOpacityPercent;
        set { backdropOpacityPercent = Math.Clamp(value, 0, 100); Changed(); Changed(nameof(BackdropAlpha)); }
    }
    public double BackdropAlpha => BackdropOpacityPercent / 100d;

    private int avatarSizePercent = 100, bubbleSizePercent = 100;
    public int AvatarSizePercent
    {
        get => avatarSizePercent;
        set { avatarSizePercent = Math.Clamp(value, 50, 150); Changed(); Changed(nameof(AvatarSide)); }
    }
    public double AvatarSide => 96 * AvatarSizePercent / 100d;
    public int BubbleSizePercent
    {
        get => bubbleSizePercent;
        set { bubbleSizePercent = Math.Clamp(value, 50, 150); Changed(); Changed(nameof(BubbleScale)); }
    }
    public double BubbleScale => BubbleSizePercent / 100d;
    private void LoadOverlayStyle()
    {
        AvatarSizePercent = activeSettings.AvatarSizePercent;
        BubbleSizePercent = activeSettings.BubbleSizePercent;
        var previous = activeSettings.OverlayOpacityPercent;
        TextOpacityPercent = activeSettings.TextOpacity ?? previous;
        AvatarOpacityPercent = activeSettings.AvatarOpacity ?? previous;
        BackdropOpacityPercent = activeSettings.BackdropOpacity ?? previous;
        TextPlacement = activeSettings.TextPlacement;
    }
    private RadioSettings WithOverlayStyle(RadioSettings current) => current with
    {
        AvatarSizePercent = AvatarSizePercent, BubbleSizePercent = BubbleSizePercent,
        TextOpacity = TextOpacityPercent, AvatarOpacity = AvatarOpacityPercent,
        BackdropOpacity = BackdropOpacityPercent, TextPlacement = TextPlacement
    };
    private void SaveOverlayStyle()
    {
        var next = WithOverlayStyle(activeSettings);
        if (next == activeSettings) return;
        try { next.Save(settingsPath); activeSettings = next; }
        catch (Exception e) { Notice = "屏显样式保存失败：" + e.Message; }
    }
    public string OverlayButtonText => overlayWindow is null ? "开启屏显" : "关闭屏显";
    public ICommand ToggleOverlayCommand { get; private set; } = null!;

    private void InitializeOverlay()
    {
        ToggleOverlayCommand = GameCommand(() =>
        {
            if (!HasGame) return Task.CompletedTask;
            if (overlayWindow is not null) overlayWindow.Close();
            else
            {
                var window = new RadioOverlay { DataContext = this };
                window.Closed += (_, _) => { overlayWindow = null; Changed(nameof(OverlayButtonText)); };
                overlayWindow = window;
                window.Show();
                Changed(nameof(OverlayButtonText));
            }
            return Task.CompletedTask;
        }, e => Notice = "屏显无法开启：" + e.Message);
    }
}
