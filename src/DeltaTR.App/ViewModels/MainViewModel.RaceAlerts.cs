using DeltaTR.Core;
using DeltaTR.Engineer;

namespace DeltaTR.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly RaceAlertMonitor raceAlerts = new();
    private GameChoice? alertGame;
    public IEnumerable<GameChoice> AlertGames => Games.Where(game => game.Id.HasValue);
    public GameChoice AlertGame
    {
        get => alertGame ?? Games[1];
        set
        {
            if (value?.Id == null || value == alertGame) return;
            alertGame = value;
            Changed();
            NotifyAlertChoices();
        }
    }

    private RaceAlertKind AlertsFor(RacingGame game) =>
        activeSettings.RaceAlerts.GetValueOrDefault(game.ToString(), RaceAlertKind.Yellow | RaceAlertKind.Position | RaceAlertKind.Penalty);
    private RaceAlertKind EditedAlerts => AlertsFor(AlertGame.Id!.Value);
    public bool AnnounceYellow { get => EditedAlerts.HasFlag(RaceAlertKind.Yellow); set => SaveAlert(RaceAlertKind.Yellow, value); }
    public bool AnnouncePosition { get => EditedAlerts.HasFlag(RaceAlertKind.Position); set => SaveAlert(RaceAlertKind.Position, value); }
    public bool AnnouncePenalty { get => EditedAlerts.HasFlag(RaceAlertKind.Penalty); set => SaveAlert(RaceAlertKind.Penalty, value); }

    public bool AnnounceBlue { get => EditedAlerts.HasFlag(RaceAlertKind.Blue); set => SaveAlert(RaceAlertKind.Blue, value); }
    public bool AnnounceGreen { get => EditedAlerts.HasFlag(RaceAlertKind.Green); set => SaveAlert(RaceAlertKind.Green, value); }
    public bool AnnouncePit { get => EditedAlerts.HasFlag(RaceAlertKind.Pit); set => SaveAlert(RaceAlertKind.Pit, value); }
    public bool AnnounceLap { get => EditedAlerts.HasFlag(RaceAlertKind.Lap); set => SaveAlert(RaceAlertKind.Lap, value); }
    public bool AnnounceSession { get => EditedAlerts.HasFlag(RaceAlertKind.Session); set => SaveAlert(RaceAlertKind.Session, value); }

    private void SaveAlert(RaceAlertKind kind, bool enabled)
    {
        var updated = enabled ? EditedAlerts | kind : EditedAlerts & ~kind;
        if (updated == EditedAlerts) return;
        var profiles = new Dictionary<string, RaceAlertKind>(activeSettings.RaceAlerts) { [AlertGame.Id!.Value.ToString()] = updated };
        var next = activeSettings with { RaceAlerts = profiles };
        try { next.Save(settingsPath); activeSettings = next; }
        catch (Exception error) { SettingsStatus = "赛事提醒设置保存失败：" + error.Message; }
        NotifyAlertChoices();
    }

    private void NotifyAlertChoices()
    {
        Changed(nameof(AnnounceYellow)); Changed(nameof(AnnouncePosition)); Changed(nameof(AnnouncePenalty));
        Changed(nameof(AnnounceBlue)); Changed(nameof(AnnounceGreen)); Changed(nameof(AnnouncePit));
        Changed(nameof(AnnounceLap)); Changed(nameof(AnnounceSession));
    }

    private string? alertSession;
    private string lastRaceAlert = "";
    public bool HasRaceAlert => LastRaceAlert.Length > 0;
    public string LastRaceAlert
    {
        get => lastRaceAlert;
        private set
        {
            if (value == lastRaceAlert) return;
            lastRaceAlert = value;
            Changed(); Changed(nameof(HasRaceAlert));
        }
    }

    private void UpdateRaceAlerts(RaceSnapshot? frame, DateTimeOffset now)
    {
        if (closing || !HasGame || stopped || frame?.Game != SelectedGame.Id ||
            frame != null && !RaceReports.Fresh(frame.CapturedAt, now)) frame = null;
        frame = raceReports.Observe(frame, now);
        radioFrame = frame;
        if (frame?.SessionId != alertSession || frame == null)
        {
            alertSession = frame?.SessionId;
            LastRaceAlert = "";
            if (bubbleTurn == BubbleTurn.Alert) { speech.StopSpeaking(); SetBubbleTurn(BubbleTurn.Idle); }
        }
        raceAlerts.Observe(frame, now, frame == null ? RaceAlertKind.None : AlertsFor(frame.Game));
        automation.Observe(frame, now, activeSettings.Automations, raceAlerts.Occurrences);
        if (frame?.Phase != SessionPhase.Live || IsListening || processing || aiStop != null || levelMonitor != null || speech.IsSpeaking) return;
        var message = raceAlerts.TakeReady(ImperialUnits);
        if (message == null)
        {
            message = automation.TakeReady(frame, now, raceReports, ImperialUnits);
            if (message != null) AutomationStatus = $"上次执行 {now.ToLocalTime():HH:mm:ss} · {message}";
        }
        if (message == null) return;
        PresentRaceMessage(message);
    }
    internal void PresentRaceMessage(string message)
    {
        LastRaceAlert = message;
        if (bubbleTurn != BubbleTurn.Typing) SetBubbleTurn(BubbleTurn.Alert);
        if (SpeakReplies) speech.Speak(message, Volume);
    }
}
