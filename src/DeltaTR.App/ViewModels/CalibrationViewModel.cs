using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DeltaTR.Core;
using DeltaTR.Core.Validation;
using DeltaTR.Storage;
using Microsoft.Win32;

namespace DeltaTR.App.ViewModels;

public sealed class CalibrationViewModel : INotifyPropertyChanged
{
    private readonly Func<RaceSnapshot?> getLive;
    private CalibrationCapture? capture;
    private CalibrationDraft? draft;
    private CalibrationMetric selectedMetric = CalibrationCatalog.Metrics[0];
    private string selectedUnit = "km/h", hudValue = "", message = "连接真实 ACC 后，先冻结当前帧，再填写同一时刻的 HUD 读数。";

    public CalibrationViewModel(Func<RaceSnapshot?> getLive)
    {
        this.getLive = getLive;
        CaptureCommand = new AsyncCommand(() => { Capture(); return Task.CompletedTask; }, Error);
        AddCommand = new AsyncCommand(() => { Add(); return Task.CompletedTask; }, Error);
        SaveCommand = new AsyncCommand(SaveAsync, Error);
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Saved;
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    private void Error(Exception e) => Message = e.Message;
    public IReadOnlyList<CalibrationMetric> Metrics => CalibrationCatalog.Metrics;
    public ObservableCollection<HudComparison> Comparisons { get; } = [];
    public ICommand CaptureCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand SaveCommand { get; }
    public string? SavedProfilePath { get; private set; }
    public string Message { get => message; private set { message = value; Changed(); } }
    public string Context => draft?.ContextLabel ?? "尚未冻结真实 ACC 数据";
    public string Progress => $"{Comparisons.Count} 条合格对照 · {draft?.ReadyFields.Length ?? 0} 个字段满足双样本要求";
    public CalibrationMetric SelectedMetric
    {
        get => selectedMetric;
        set
        {
            if (value is null) return;
            selectedMetric = value;
            Changed(); Changed(nameof(Units));
            SelectedUnit = value.Units[0]; HudValue = "";
            Changed(nameof(RawValue)); Changed(nameof(Tolerance));
        }
    }
    public string[] Units => SelectedMetric.Units;
    public string SelectedUnit { get => selectedUnit; set { selectedUnit = value; Changed(); } }
    public string HudValue { get => hudValue; set { hudValue = value; Changed(); } }
    public string SampleNote { get; set; } = "";
    public string ExperimentNote { get; set; } = "";
    public string Tolerance => $"允许偏差 ≤ {SelectedMetric.Tolerance}；数值按右侧所选单位填写，不自动换算。";
    public string RawValue => capture?.Snapshot.Fields.TryGetValue(SelectedMetric.Key, out var v) == true
        ? $"原始值 {v.Value?.ToString("0.###", CultureInfo.InvariantCulture)} {v.Unit} · {v.Quality}\n来源 {v.Source}\n观测 {v.ObservedAt:O}"
        : "冻结帧尚无该字段";

    private void Capture()
    {
        var snapshot = getLive() ?? throw new InvalidOperationException("请关闭向导并连接 ACC 实时驾驶；演示、回放或已停止的数据源不能校准。");
        var next = CalibrationCapture.Freeze(snapshot, DateTimeOffset.UtcNow);
        capture = next;
        draft ??= new(next);
        HudValue = "";
        Message = "已冻结。请填写当时 HUD 的读数与场景备注，再加入对照。";
        Changed(nameof(Context)); Changed(nameof(RawValue));
    }

    private void Add()
    {
        if (capture is null || draft is null) throw new InvalidOperationException("请先冻结遥测。");
        if (!double.TryParse(HudValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            throw new ArgumentException("HUD 读数必须是数字，小数点使用英文句点。");
        draft.Add(capture, SelectedMetric.Key, number, SelectedUnit, SampleNote);
        Comparisons.Add(draft.Comparisons[^1]);
        Message = "已记录人工对照。每个字段需至少两个不同来源帧；请在另一时刻再次冻结。";
        Changed(nameof(Progress));
    }

    private async Task SaveAsync()
    {
        if (draft is null) throw new InvalidOperationException("没有可保存的校准草稿。");
        draft.Export(ExperimentNote, DateTimeOffset.UtcNow);
        var dialog = new SaveFileDialog { Filter = "校准配置|*.json", FileName = $"acc-{DateTime.Now:yyyyMMdd-HHmmss}.local.json" };
        if (dialog.ShowDialog() != true) return;
        await CalibrationBundle.SaveAsync(draft, dialog.FileName, ExperimentNote);
        SavedProfilePath = dialog.FileName;
        Saved?.Invoke();
    }
}
