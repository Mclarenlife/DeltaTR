using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using DeltaTR.Core;
using DeltaTR.Engineer;
using DeltaTR.Telemetry.Abstractions;
using DeltaTR.Telemetry.ACC;
using Microsoft.Win32;

namespace DeltaTR.App.ViewModels;

public sealed record FieldRow(string Name, string Value, string Unit, string Quality, string Detail);
public sealed record WheelRow(string Name, string Values, string Quality, string NativeValues);

public sealed partial class MainViewModel : INotifyPropertyChanged
{
    private readonly StateStore store = new();
    private TelemetryQueries queries = new();
    private readonly DispatcherTimer refresh;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private CancellationTokenSource? stop;
    private Task? sampling;

    private CalibrationProfile calibration = new();
    private bool isDemo, stopped = true;

    private RaceSnapshot? shown;
    private bool showConnectionNotice;
    private string notice = "按住讲话键，松开发送 · 中文离线识别 · 不保存音频";
    private string answer = "等待你的查询。进入驾驶后即可询问排名、圈数和车速。";
    private string modeLabel = "ACC / 未连接";
    private NavigationItem selectedPage;
    private string fieldFilter = "";
    private bool compactTables, showTechnicalDetails;

    public IReadOnlyList<NavigationItem> Navigation { get; } = [
        new("radio", "无线电", "按住讲话，车队工程师随时回应。", "\uE720"),
        new("overview", "车辆与赛况", "关注车辆状态，掌握当前赛况。", "\uE9D9"),
        new("vocabulary", "自定义词库", "为遥测项目设置习惯的问法。", "\uE8F1"),
        new("strategy", "策略组", "定制 AI 对话偏好，分析进站、换胎与配速。", "\uE9CE"),
        new("automation", "自动化", "按比赛事件或时间间隔，让工程师依次播报。", "\uE713"),
        new("overlay", "屏显", "在驾驶画面上查看无线电对话。", "\uE7F4"),
        new("settings", "设置", "连接、语音和数据校准。", "\uE713")
    ];

    public MainViewModel(string? settingsFile = null, System.Net.Http.HttpClient? chatClient = null)
    {
        settingsPath = settingsFile ?? Settings.RadioSettings.FileName;
        this.chatClient = chatClient;
        selectedPage = Navigation[0];
        AsyncCommand Command(Func<Task> action) => GameCommand(action, e => Notice = "操作失败：" + e.Message);
        LiveCommand = Command(async () =>
        {
            if (!HasGame) return;
            Notice = $"正在检查 {GameName} 遥测…";
            await StartAsync(false);
            var check = sampling;
            await Task.Delay(220);
            if (check == sampling && !stopped && !isDemo)
            {
                showConnectionNotice = true;
                Refresh();
            }
        });
        EnableBroadcastCommand = Command(() =>
        {
            if (!AccFeatures) return Task.CompletedTask;
            Notice = DeltaTR.Telemetry.ACC.Broadcast.BroadcastConfig.Enable();
            return Task.CompletedTask;
        });
        DemoCommand = Command(() => StartAsync(true));
        StopCommand = Command(StopAsync);
        CalibrationCommand = Command(LoadCalibrationAsync);
        CalibrationWizardCommand = Command(OpenCalibrationWizardAsync);
        QueryCommand = Command(() => IsListening || processing ? Task.CompletedTask : SubmitQuestionAsync(Question));
        OpenRadioCommand = Command(() => { SelectedPage = Navigation[0]; return Task.CompletedTask; });
        refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        refresh.Tick += (_, _) => Refresh();
        refresh.Start();
        InitializeSettings();
        InitializeVocabulary();
        InitializeVoice();
        InitializeOverlay();
        InitializeWheelScreen();
        InitializeAutomation();
        InitializeStrategy();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    public ICommand LiveCommand { get; }
    public string BroadcastStatus => isDemo && !stopped ? "当前使用演示数据" : shown?.Broadcast?.Status ?? "广播未连接";
    public ICommand EnableBroadcastCommand { get; }
    public ICommand DemoCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand CalibrationCommand { get; }
    public ICommand CalibrationWizardCommand { get; }
    public ICommand QueryCommand { get; }
    public ICommand OpenRadioCommand { get; }
    public NavigationItem SelectedPage
    {
        get => selectedPage;
        set
        {
            if (value is null || value == selectedPage) return;
            if (selectedPage.Key == "settings") StopMicrophoneTest();
            if (selectedPage.Key == "overlay") SaveOverlayStyle();
            selectedPage = value;
            Changed();
        }
    }
    public string FieldFilter
    {
        get => fieldFilter;
        set { fieldFilter = value; Changed(); Changed(nameof(FilteredRows)); Changed(nameof(EmptyFieldMessage)); }
    }
    public IReadOnlyList<FieldRow> FilteredRows => Rows.Where(r => string.IsNullOrWhiteSpace(FieldFilter) ||
        r.Name.Contains(FieldFilter, StringComparison.OrdinalIgnoreCase) || r.Quality.Contains(FieldFilter, StringComparison.OrdinalIgnoreCase)).ToArray();
    public string EmptyFieldMessage => string.IsNullOrWhiteSpace(FieldFilter) ? $"暂无遥测数据。可以启动演示或连接 {GameName}。" : "没有匹配的字段，请尝试其他名称或质量状态。";
    public bool CompactTables { get => compactTables; set { compactTables = value; Changed(); Changed(nameof(FieldRowHeight)); } }
    public double FieldRowHeight => CompactTables ? 30 : 42;
    public bool ShowTechnicalDetails { get => showTechnicalDetails; set { showTechnicalDetails = value; Changed(); } }
    public string SourceLabel => !HasGame ? "请选择游戏" : stopped ? "已停止" : isDemo ? "演示数据" : shown?.Phase switch
    {
        SessionPhase.Live => $"{GameName} 已连接 · 驾驶中",
        SessionPhase.Paused => $"{GameName} 已连接 · 暂停",
        SessionPhase.Replay => $"{GameName} 已连接 · 重播",
        SessionPhase.Idle => $"{GameName} 已连接 · 待驾驶",
        SessionPhase.Disconnected => $"{GameName} 未连接",
        SessionPhase.Faulted => $"{GameName} 读取异常",
        _ when shown?.Broadcast?.Driving == true => "赛事广播已连接 · 等待本车遥测",
        _ => $"正在等待 {GameName} 遥测"
    };
    public string SourceDescription => stopped ? "数据源已停止 · 可以重新连接" : isDemo ? "合成数据 · 仅供界面与流程体验" :
        shown?.Phase switch
        {
            SessionPhase.Live => $"{Car} · {Track} · {Status}",
            SessionPhase.Paused => $"{Car} · {Track} · 游戏已暂停，请返回驾驶画面并解除暂停。",
            SessionPhase.Idle => "已读取游戏遥测，进入练习或比赛并点击驾驶。",
            SessionPhase.Replay => "正在读取重播，返回驾驶会话后可以查询实时车况。",
            _ => shown?.StatusMessage ?? "正在检查游戏进程与共享遥测…"
        };
    public string ConnectLabel => stopped || isDemo ? $"连接 {GameName}" : $"重新检测 {GameName}";
    public string CalibrationSummary => calibration.VerifiedUnits.Count == 0 ? "当前使用内置读取规则" : $"已加载 {calibration.VerifiedUnits.Count} 项字段配置";
    private string question = "现在胎压怎么样";
    public string Question { get => question; set { if (question == value) return; question = value; Changed(); SetTyping(!string.IsNullOrWhiteSpace(value)); } }
    public string Answer { get => answer; private set { answer = value; Changed(); Changed(nameof(BubbleText)); } }
    public string Notice { get => showConnectionNotice ? SourceDescription : notice; private set { showConnectionNotice = false; notice = value; Changed(); } }
    public string ModeLabel { get => modeLabel; private set { modeLabel = value; Changed(); } }
    public string Status => shown?.StatusMessage ?? "等待数据源";
    public string Car => string.IsNullOrEmpty(shown?.CarModel) ? "—" : shown.CarModel;
    public string Track => string.IsNullOrEmpty(shown?.Track) ? "—" : shown.Track;
    public string FrameInfo => shown is null ? "20 Hz 采样 / 5 Hz 显示" : $"{shown.Phase}  ·  P {shown.PhysicsPacketId} / G {shown.GraphicsPacketId}";
    public string Speed => Value("speed");
    public string Fuel => Value("fuel");
    public string Position => shown?.Fields.TryGetValue("position", out var p) == true && shown.Phase == SessionPhase.Live && p.IsUsable(DateTimeOffset.UtcNow, Metrics.MaxAge) ? $"P{p.Value:0}" : "—";
    public string RaceBrief => queries.Answer("赛场状况", stopped ? null : shown, DateTimeOffset.UtcNow).Text;
    public string SpeedQuality => Quality("speed");
    public string FuelQuality => Quality("fuel");
    public string ValidCount => shown is null ? "—" : $"{shown.Fields.Values.Count(v => v.IsUsable(DateTimeOffset.UtcNow, Metrics.MaxAge))} / {shown.Fields.Count}";
    public IReadOnlyList<FieldRow> Rows { get; private set; } = [];
    public IReadOnlyList<WheelRow> Wheels { get; private set; } = [];

    private string Value(string key) => shown?.Phase == SessionPhase.Live && shown.Fields.TryGetValue(key, out var v) && v.IsUsable(DateTimeOffset.UtcNow, Metrics.MaxAge) ?
        $"{v.Value?.ToString("0.0", CultureInfo.InvariantCulture) ?? "—"} {v.Unit}" : "—";
    private string Quality(string key) => shown?.Fields.TryGetValue(key, out var v) == true ? v.IsUsable(DateTimeOffset.UtcNow, Metrics.MaxAge) ? "数据可用" : v.Quality == DataQuality.NeedsVerification ? "待核验" : "暂无有效读数" : "暂无数据";

    private static string NativeWheelReading(RaceSnapshot? snapshot, string wheel, DateTimeOffset now)
    {
        if (snapshot?.Phase != SessionPhase.Live ||
            !snapshot.Fields.TryGetValue($"temperature.{wheel}", out var temperature) ||
            !snapshot.Fields.TryGetValue($"pressure.{wheel}", out var pressure) ||
            !temperature.IsUsable(now, Metrics.MaxAge) || !pressure.IsUsable(now, Metrics.MaxAge))
            return "";
        return $"原始  {temperature.Value:0.0} {temperature.Unit} / {pressure.Value:0.0} {pressure.Unit}";
    }

    private void Refresh()
    {
        var native = store.Latest;
        var now = DateTimeOffset.UtcNow;
        shown = DisplayUnits.ForDisplay(native, ImperialUnits);
        UpdateSessionCatalog(shown);
        UpdateStrategy(stopped ? null : native, now);
        UpdateRaceAlerts(native, now);
        UpdateOverlaySession();
        RefreshLocalAnswer(shown, now);
        var local = shown?.Fields.Concat(shown.LocalFields);
        if (shown?.Broadcast is { } feed && local is not null) local = local.Concat(feed.Fields);
        Rows = local?.Select(kv => new FieldRow(kv.Key, kv.Value.TextValue ?? kv.Value.Value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "—",
            kv.Value.Unit, kv.Value.Quality.ToString(), $"{kv.Value.Source}\n观测时间 {kv.Value.ObservedAt:O}\n游戏版本 {kv.Value.GameVersion}")).ToArray() ?? [];
        var tempKey = SelectedGame.Id == RacingGame.IRacing ? "pitTemp" : "temperature";
        var pressureKey = SelectedGame.Id == RacingGame.IRacing ? "coldPressure" : "pressure";
        Wheels = Metrics.Wheels.Select(w => new WheelRow(w, Value($"{tempKey}.{w}") + " / " + Value($"{pressureKey}.{w}"),
            Quality($"{tempKey}.{w}") + " / " + Quality($"{pressureKey}.{w}"), SelectedGame.Id == RacingGame.IRacing
                ? "上次测量中部剩余胎面  " + Value($"pitTread.{w}") : NativeWheelReading(native, w, now))).ToArray();
        foreach (var name in new[] { nameof(Status), nameof(Car), nameof(Track), nameof(FrameInfo), nameof(Speed), nameof(Fuel), nameof(SpeedQuality), nameof(FuelQuality), nameof(ValidCount), nameof(Rows), nameof(FilteredRows), nameof(Wheels), nameof(SourceLabel), nameof(ConnectLabel), nameof(BroadcastStatus), nameof(NativeRaceStatus), nameof(SourceDescription), nameof(CalibrationSummary) }) Changed(name);
        if (showConnectionNotice) Changed(nameof(Notice));
        if (sampling?.IsFaulted == true) Notice = "采集已失败：" + sampling.Exception?.GetBaseException().Message;
        if (SelectedPage.Key == "vocabulary") Changed(nameof(FieldAvailability));
        Changed(nameof(Position)); Changed(nameof(RaceBrief));
    }

    public Task StartAsync(bool demo) => StartAsync(demo, false);
    private async Task StartAsync(bool demo, bool duringSwitch)
    {
        if ((!duringSwitch && !HasGame) || SelectedGame.Id is not { } game) return;
        await lifecycle.WaitAsync();
        try
        {
            if ((!duringSwitch && !HasGame) || SelectedGame.Id != game || closing) return;
            liveQuestion = null;
            CancelVoice(); await StopInternalAsync();
            if ((!duringSwitch && !HasGame) || SelectedGame.Id != game || closing) return;
            isDemo = demo; stopped = false;
            Refresh();
            stop = new();
            var token = stop.Token;
            ModeLabel = demo ? "SYNTHETIC / 演示 · 非真实遥测" : $"{GameName} / 实时只读遥测";
            LastQuestion = "等待你的呼叫";
            Answer = demo ? $"{GameName} 演示数据已接通。" : $"等待 {GameName} 遥测。进入驾驶后可以直接询问车况与赛况。";
            IGameAdapter adapter = CreateAdapter(demo, game);
            sampling = Task.Run(async () =>
            {
                using (adapter)
                using (var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(50)))
                {

                    do
                    {
                        var snapshot = adapter.Read();
                        store.Publish(snapshot);

                    } while (await timer.WaitForNextTickAsync(token));
                }
            }, token);
        }
        finally { lifecycle.Release(); }
    }

    private async Task LoadCalibrationAsync()
    {
        if (!AccFeatures) return;
        var dialog = new OpenFileDialog { Filter = "校准记录|*.json" };
        if (dialog.ShowDialog() != true) return;
        var next = CalibrationProfile.Load(dialog.FileName);
        calibration = next;
        Notice = "已加载校准文件；仅匹配版本、车型、赛道和证据的字段生效。";
        if (!stopped) await StartAsync(isDemo);
    }

    private async Task OpenCalibrationWizardAsync()
    {
        CancelVoice();
        if (!AccFeatures) return;
        var vm = new CalibrationViewModel(() => !isDemo && !stopped ? store.Latest : null);
        var window = new CalibrationWindow(vm) { Owner = System.Windows.Application.Current.MainWindow };
        if (window.ShowDialog() != true || vm.SavedProfilePath is null) return;
        calibration = CalibrationProfile.Load(vm.SavedProfilePath);
        Notice = "已保存证据并加载校准：" + vm.SavedProfilePath;
        if (!stopped) await StartAsync(isDemo);
    }

    private async Task StopInternalAsync()
    {
        stop?.Cancel();
        try { if (sampling is not null) await sampling; }
        catch (OperationCanceledException) { }
        catch (Exception e) { Notice = "采集结束：" + e.Message; }
        finally { stop?.Dispose(); stop = null; sampling = null; stopped = true; store.Clear(); }

    }

    public async Task StopAsync()
    {
        await lifecycle.WaitAsync();
        try { liveQuestion = null; CancelVoice(); await StopInternalAsync(); Answer = "数据源已断开，请重新连接后查询。"; ModeLabel = "STOPPED / 已停止"; Refresh(); }
        finally { lifecycle.Release(); }
    }

    public async Task ShutdownAsync()
    {
        SaveOverlayStyle();
        overlayWindow?.Close();
        closing = true; refresh.Stop(); voiceTimer.Stop();
        await pitHouseBridge.DisposeAsync();
        await wheelScreen.DisposeAsync();
        try { await StopAsync(); } catch (Exception e) { Notice = "关闭时错误：" + e.Message; }
        finally { speech.Dispose(); await zipformer.UnloadAsync(); await senseVoice.UnloadAsync(); }
    }
}
