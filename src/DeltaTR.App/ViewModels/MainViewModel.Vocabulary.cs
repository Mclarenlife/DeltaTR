using System.Windows.Input;
using DeltaTR.Engineer;
using DeltaTR.Core;

namespace DeltaTR.App.ViewModels;

public sealed record VocabularyEntry(RadioField Field, bool IsPinned);

public sealed partial class MainViewModel
{
    private RadioField selectedField = RadioVocabulary.Fields[0];
    private readonly Dictionary<string, string> drafts = [];
    private string vocabularyStatus = "";
    public IReadOnlyList<RadioField> VocabularyFields { get; private set; } = [];
    private IReadOnlyList<NativeField>? currentCatalog;
    private void UpdateSessionCatalog(RaceSnapshot? snapshot)
    {
        if (!HasGame || SelectedGame.Id != RacingGame.IRacing) return;
        var next = snapshot?.Game == RacingGame.IRacing ? snapshot.Native?.Catalog : null;
        if (ReferenceEquals(currentCatalog,next)) return;
        currentCatalog=next;
        VocabularyFields=RadioVocabulary.For(RacingGame.IRacing,next);
        try { queries=new TelemetryQueries(CurrentPhrases,activeSettings.WakeWord,RacingGame.IRacing,next); }
        catch (ArgumentException e)
        {
            queries=new TelemetryQueries(new Dictionary<string,string[]>(),"",RacingGame.IRacing,next);
            VocabularyStatus="会话词库存在冲突，已使用内置问法：" + e.Message;
        }
        SelectedField=VocabularyFields.FirstOrDefault(f=>f.Key==SelectedField.Key)??VocabularyFields[0];
        Changed(nameof(VocabularyFields)); Changed(nameof(CatalogSummary)); Changed(nameof(FilteredVocabulary)); Changed(nameof(PhraseEditor));
    }
    private string vocabularyFilter = "";
    public string VocabularyFilter
    {
        get => vocabularyFilter;
        set
        {
            vocabularyFilter = value.Trim();
            Changed(nameof(FilteredVocabulary));
            var entries = FilteredVocabulary;
            if (!entries.Any(e => e.Field == selectedField) && entries.FirstOrDefault() is { } first) SelectedField = first.Field;
        }
    }
    public IReadOnlyList<VocabularyEntry> FilteredVocabulary
    {
        get
        {
            var pinned = activeSettings.PinnedVocabulary.GetValueOrDefault(GameName);
            return VocabularyFields.Where(f => f.Title.Contains(vocabularyFilter, StringComparison.OrdinalIgnoreCase) ||
                    f.Group.Contains(vocabularyFilter, StringComparison.OrdinalIgnoreCase) || f.Key.Contains(vocabularyFilter, StringComparison.OrdinalIgnoreCase))
                .Select(f => new VocabularyEntry(f, pinned?.Contains(f.Key) == true)).OrderByDescending(row => row.IsPinned).ToArray();
        }
    }

    internal void SetVocabularyPinned(IEnumerable<VocabularyEntry> entries, bool pin)
    {
        if (!HasGame) return;
        var keys = new HashSet<string>(activeSettings.PinnedVocabulary.GetValueOrDefault(GameName) ?? []);
        bool changed = false;
        foreach (var entry in entries) changed |= pin ? keys.Add(entry.Field.Key) : keys.Remove(entry.Field.Key);
        if (!changed) return;
        var next = activeSettings with { PinnedVocabulary = new(activeSettings.PinnedVocabulary) { [GameName] = keys } };
        try { next.Save(settingsPath); }
        catch (Exception e) { VocabularyStatus = "置顶设置保存失败：" + e.Message; return; }
        activeSettings = next;
        Changed(nameof(FilteredVocabulary));
        Changed(nameof(SelectedField));
    }
    public RadioField SelectedField
    {
        get => selectedField;
        set
        {
            if (value is null || value == selectedField) return;
            selectedField = value;
            Changed(); Changed(nameof(PhraseEditor)); Changed(nameof(FieldAvailability));
        }
    }
    public string CatalogSummary => SelectedGame.Id == RacingGame.IRacing && (currentCatalog == null || isDemo)
        ? $"离线参考词库 · {VocabularyFields.Count} 项，连接后按 SDK 变量更新"
        : $"当前游戏词库 · {VocabularyFields.Count} 项";
    public string FieldAvailability
    {
        get
        {
            var key = SelectedField.Key;
            var keys = key is "temperature" or "pressure" ? Core.Metrics.Wheels.Select(w => key + "." + w) :
                Core.AccChannels.WheelGroups.TryGetValue(key, out var wheels) ? wheels.Select(w => w.Key) : [key];
            if (shown is null) return "等待数据源提供此项。";
            var readings = keys.Select(shown.Reading).ToArray();
            if (readings.Any(m => m is null)) return "当前数据源暂未提供此项。";
            var now = DateTimeOffset.UtcNow;
            if (readings.Any(m => !m!.IsUsable(now,Metrics.MaxAge))) return "字段已接入，等待有效读数。";
            var value = string.Join("、",readings.Select(m => m!.TextValue ?? m.Value?.ToString("0.###"))) + " " + readings[0]!.Unit;
            return "当前读数 · " + (value.Length > 120 ? value[..120] + "…" : value.TrimEnd());
        }
    }
    public string PhraseEditor
    {
        get => drafts.TryGetValue(selectedField.Key, out var draft) ? draft : string.Join(Environment.NewLine, queries.FieldPhrases(selectedField.Key));
        set { drafts[selectedField.Key] = value; VocabularyStatus = "有未保存的修改"; Changed(); }
    }
    public string VocabularyStatus { get => vocabularyStatus; private set { vocabularyStatus = value; Changed(); } }
    public ICommand SaveVocabularyCommand { get; private set; } = null!;
    public ICommand ResetFieldCommand { get; private set; } = null!;

    private void InitializeVocabulary()
    {
        SaveVocabularyCommand = GameCommand(() =>
        {
            if (!HasGame || SelectedGame.Id is not { } game) return Task.CompletedTask;
            var updates = new Dictionary<string, string[]>(CurrentPhrases);
            var defaults = new TelemetryQueries(new Dictionary<string,string[]>(), "", game, currentCatalog);
            foreach (var (metric, draft) in drafts)
            {
                var lines = draft.Split(['\r','\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (lines.SequenceEqual(defaults.FieldPhrases(metric))) updates.Remove(metric);
                else updates[metric] = lines;
            }
            var nextQueries = new TelemetryQueries(updates, activeSettings.WakeWord, game, currentCatalog);
            var next = game == RacingGame.ACC ? activeSettings with { PhraseOverrides = updates } :
                activeSettings with { GamePhrases = new(activeSettings.GamePhrases) { [GameName] = updates } };
            next.Save(settingsPath);
            CancelVoice();
            activeSettings = next;
            queries = nextQueries;
            drafts.Clear();
            Changed(nameof(PhraseEditor));
            VocabularyStatus = "已保存，下一次呼叫将使用新词库。";
            return Task.CompletedTask;
        }, e => VocabularyStatus = "未保存：" + e.Message);
        ResetFieldCommand = GameCommand(() =>
        {
            PhraseEditor = string.Join(Environment.NewLine, new TelemetryQueries(new Dictionary<string,string[]>(), "", SelectedGame.Id ?? RacingGame.ACC, currentCatalog).FieldPhrases(selectedField.Key));
            return Task.CompletedTask;
        }, e => VocabularyStatus = e.Message);
    }
}
