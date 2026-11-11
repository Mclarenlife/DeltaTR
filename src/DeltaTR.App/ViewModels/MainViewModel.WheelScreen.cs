using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using DeltaTR.Hardware.Moza;
using Microsoft.Win32;

namespace DeltaTR.App.ViewModels;

public sealed record WheelConnectionMode(bool Bridge, string Label);

public sealed partial class MainViewModel
{
    private readonly WheelScreen wheelScreen = new();
    private readonly PitHouseBridge pitHouseBridge = new();
    private readonly ScreenPresentation screenPresentation = new();
    private long screenTurn;
    public bool WheelSmartSwitch
    {
        get => activeSettings.WheelSmartSwitch;
        set
        {
            if (value == WheelSmartSwitch) return;
            var next = activeSettings with { WheelSmartSwitch = value };
            try { next.Save(settingsPath); } catch (Exception e) { WheelStatus = e.Message; Changed(); return; }
            activeSettings = next; screenPresentation.Reset(); screenTurn++;
            Changed(); Changed(nameof(TalkHint)); UpdateWheelScreen();
        }
    }
    public bool WheelAutoPopup
    {
        get => activeSettings.WheelAutoPopup;
        set
        {
            if (value == WheelAutoPopup) return;
            var next = activeSettings with { WheelAutoPopup = value };
            try { next.Save(settingsPath); } catch (Exception e) { WheelStatus = e.Message; Changed(); return; }
            activeSettings = next; Changed(); UpdateWheelScreen();
        }
    }
    internal bool UseSmartWheelButton => UsePitHouseBridge && pitHouseBridge.IsRunning && WheelSmartSwitch;
    private bool WheelActivity => IsListening || processing || aiStop is not null || bubbleTurn is BubbleTurn.Listening or BubbleTurn.Reply or BubbleTurn.Alert;
    internal void ToggleWheelPresentation()
    {
        bool desired = screenPresentation.Show(WheelAutoPopup, WheelActivity, screenTurn);
        screenPresentation.Toggle(desired || pitHouseBridge.IsRadioVisible == true, screenTurn);
        UpdateWheelScreen();
    }
    internal void BeginWheelVoice()
    {
        screenPresentation.BeginVoice(); screenTurn++; UpdateWheelScreen();
    }
    private bool usePitHouseBridge = true;
    public IReadOnlyList<WheelConnectionMode> WheelConnectionModes { get; } = [new(true, "保留 Pit House · 实验共存"), new(false, "独立连接 · 需退出 Pit House")];
    public bool UsePitHouseBridge
    {
        get => usePitHouseBridge;
        set { if (IsAnyWheelScreenRunning || value == usePitHouseBridge) return; usePitHouseBridge = value; Changed(); Changed(nameof(WheelConnectionHelp)); }
    }
    private bool IsAnyWheelScreenRunning => wheelScreen.IsRunning || pitHouseBridge.IsRunning;
    public bool CanChangeWheelMode => !IsAnyWheelScreenRunning;
    public string WheelConnectionHelp => UsePitHouseBridge
        ? "上传 DeltaTR KS Pro Fullscreen Radio 仪表，安装或升级桥接并重启 Pit House。先在 Pit House 选好比赛仪表，再开启屏显。使用专用文字字段；短按讲话键切换画面，长按 0.5 秒开始讲话，松开发送。"
        : "先应用 DeltaTR KS Pro Fullscreen Radio 仪表，再退出 Pit House。独立模式会占用基座串口。";
    private string pitHousePath = "";
    public string PitHousePath { get => pitHousePath; set { pitHousePath = value; Changed(); } }
    public ICommand ChoosePitHouseCommand { get; private set; } = null!;
    public ICommand InstallPitHouseBridgeCommand { get; private set; } = null!;
    public ICommand RestorePitHouseBridgeCommand { get; private set; } = null!;
    private IReadOnlyList<WheelPort> wheelPorts = [];
    public IReadOnlyList<WheelPort> WheelPorts { get => wheelPorts; private set { wheelPorts = value; Changed(); } }
    public WheelPort? SelectedWheelPort { get; set; }
    private string wheelStatus = "尚未开启 · 请先上传 DeltaTR KS Pro Fullscreen Radio 仪表；智能切换模式下，开启前选好比赛仪表";
    public string WheelStatus { get => wheelStatus; private set { if (wheelStatus != value) { wheelStatus = value; Changed(); } } }
    public string WheelButtonText => IsAnyWheelScreenRunning ? "关闭方向盘屏显" : "开启方向盘屏显";
    public ICommand ToggleWheelScreenCommand { get; private set; } = null!;
    public ICommand RefreshWheelPortsCommand { get; private set; } = null!;
    public ICommand OpenWheelDashboardCommand { get; private set; } = null!;
    private void InitializeWheelScreen()
    {
        PitHousePath = PitHouseBridgeInstaller.FindExecutable();
        ChoosePitHouseCommand = GameCommand(() =>
        {
            var dialog = new OpenFileDialog { Title = "选择 Pit House 的 bin 目录内程序", Filter = "Pit House|MOZA Pit House.exe" };
            if (dialog.ShowDialog() == true)
            {
                var file = dialog.FileName;
                var nested = Path.Combine(Path.GetDirectoryName(file)!, "bin", "MOZA Pit House.exe");
                PitHousePath = File.Exists(nested) ? nested : file;
            }
            return Task.CompletedTask;
        }, e => WheelStatus = e.Message);
        InstallPitHouseBridgeCommand = GameCommand(async () =>
        {
            await StopWheelScreensAsync();
            if (string.IsNullOrWhiteSpace(PitHousePath)) throw new InvalidOperationException("请先选择 Pit House 程序。");
            PitHouseBridgeInstaller.Install(PitHousePath, BridgeSettings.LoadOrCreate());
            WheelStatus = "桥接已安装并备份原文件 · 请重启一次 Pit House，再开启屏显";
        }, e => WheelStatus = "桥接安装失败：" + e.Message);
        RestorePitHouseBridgeCommand = GameCommand(async () =>
        {
            await StopWheelScreensAsync();
            PitHouseBridgeInstaller.Restore();
            WheelStatus = "Pit House 原文件已恢复 · 重启 Pit House 后完成卸载";
        }, e => WheelStatus = "恢复失败：" + e.Message);
        RefreshWheelPortsCommand = GameCommand(() => { RefreshWheelPorts(); return Task.CompletedTask; }, e => WheelStatus = e.Message);
        OpenWheelDashboardCommand = GameCommand(() =>
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "Hardware", "KsPro", "DeltaTR KS Pro");
            if (!Directory.Exists(folder)) throw new DirectoryNotFoundException("仪表文件缺失，请使用完整的 DeltaTR 发布目录。");
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            return Task.CompletedTask;
        }, e => WheelStatus = e.Message);
        ToggleWheelScreenCommand = GameCommand(async () =>
        {
            if (IsAnyWheelScreenRunning) { await StopWheelScreensAsync(); WheelStatus = "方向盘屏显已关闭"; }
            else if (UsePitHouseBridge)
            {
                if (!PitHouseBridgeInstaller.IsInstalled) throw new InvalidOperationException("请安装或升级桥接并重启 Pit House，确认 Fullscreen Radio 已上传；智能切换模式下先选好比赛仪表。");
                UpdateWheelScreen();
                pitHouseBridge.Start(BridgeSettings.LoadOrCreate());
                WheelStatus = pitHouseBridge.Status;
            }
            else
            {
                RefreshWheelPorts();
                if (SelectedWheelPort is null) { WheelStatus = "未找到 MOZA 基座，请检查 USB 连接。"; return; }
                UpdateWheelScreen();
                try { await wheelScreen.StartAsync(SelectedWheelPort.Port); }
                catch (Exception e) { WheelStatus = e.Message; }
            }
            Changed(nameof(WheelButtonText));
            Changed(nameof(CanChangeWheelMode));
            Changed(nameof(TalkHint));
        }, e => WheelStatus = e.Message);
        try { RefreshWheelPorts(); } catch (Exception e) { WheelStatus = "设备检查失败：" + e.Message; }
    }
    private async Task StopWheelScreensAsync()
    {
        await pitHouseBridge.StopAsync();
        await wheelScreen.StopAsync();
        screenPresentation.Reset(); screenTurn++;
        Changed(nameof(TalkHint));
        Changed(nameof(WheelButtonText)); Changed(nameof(CanChangeWheelMode));
    }
    private void RefreshWheelPorts()
    {
        string? selected = SelectedWheelPort?.Port;
        WheelPorts = WheelScreen.FindPorts();
        SelectedWheelPort = WheelPorts.FirstOrDefault(p => p.Port == selected) ?? WheelPorts.FirstOrDefault();
        Changed(nameof(SelectedWheelPort));
    }
    private void UpdateWheelScreen()
    {
        string state = !HasGame ? "offline" : IsListening ? "listen" : processing || aiStop is not null ? "think" :
            wheelInputError.Length > 0 && bubbleTurn == BubbleTurn.Listening ? "think" :
            bubbleTurn is BubbleTurn.Reply or BubbleTurn.Alert ? "reply" : overlaySession is null ? "offline" : "idle";
        string text = state switch
        {
            "listen" => wheelInputText.Length > 0 ? wheelInputText : "请讲话…",
            "think" => wheelInputError.Length > 0 ? wheelInputError : aiStop is not null ? LastQuestion : wheelInputText.Length > 0 ? wheelInputText : "正在识别…",
            "offline" => "未连接游戏",
            "reply" => bubbleTurn == BubbleTurn.Alert ? LastRaceAlert : Answer, _ => ""
        };
        var message = new WheelMessage(state, text, unchecked((int)screenTurn));
        wheelScreen.Update(message);
        bool show = screenPresentation.Show(WheelAutoPopup, WheelActivity, screenTurn);
        pitHouseBridge.Update(message, WheelSmartSwitch ? show ? "radio" : "return" : "keep", screenTurn);
        if (pitHouseBridge.IsRunning || pitHouseBridge.HasFailed) WheelStatus = pitHouseBridge.Status;
        else if (wheelScreen.IsRunning) WheelStatus = wheelScreen.Status;
        else if (wheelScreen.Status.StartsWith("方向盘连接失败", StringComparison.Ordinal)) WheelStatus = wheelScreen.Status;
        Changed(nameof(WheelButtonText));
        Changed(nameof(CanChangeWheelMode));
    }
}
