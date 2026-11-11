using System.Collections.ObjectModel;
using System.Windows.Input;
using DeltaTR.Engineer;

namespace DeltaTR.App.ViewModels;

public sealed record TriggerChoice(RadioTrigger Id, string Name);
public sealed record ActionChoice(RadioAction Id, string Name);
public sealed record AutomationChoice(AutomationRule Rule)
{
    public string Name => Rule.Name;
    public string Summary => (Rule.Enabled ? "已启用 · " : "已停用 · ") +
        (Rule.Trigger == RadioTrigger.Interval ? $"每隔 {Rule.IntervalSeconds} 秒" : AutomationLabels.Trigger(Rule.Trigger)) +
        $" · {Rule.Actions.Length} 个动作";
}

public sealed partial class MainViewModel
{
    private readonly RadioAutomation automation = new();
    private readonly RaceReports raceReports = new();
    private Core.RaceSnapshot? radioFrame;
    public IReadOnlyList<TriggerChoice> AutomationTriggers { get; } =
        Enum.GetValues<RadioTrigger>().Select(t => new TriggerChoice(t, AutomationLabels.Trigger(t))).ToArray();
    public IReadOnlyList<ActionChoice> AutomationActions { get; } =
        Enum.GetValues<RadioAction>().Select(a => new ActionChoice(a, AutomationLabels.Action(a))).ToArray();
    public ObservableCollection<AutomationChoice> Automations { get; } = [];
    public ObservableCollection<ActionChoice> ActionSequence { get; } = [];
    private AutomationChoice? selectedAutomation;
    public AutomationChoice? SelectedAutomation
    {
        get => selectedAutomation;
        set { selectedAutomation = value; Changed(); if (value != null) LoadAutomation(value.Rule); }
    }
    private string automationName = "", automationInterval = "120", automationStatus = "";
    private bool automationEnabled = true;
    private TriggerChoice? automationTrigger;
    public string AutomationName { get => automationName; set { automationName = value; Changed(); } }
    public string AutomationInterval { get => automationInterval; set { automationInterval = value; Changed(); } }
    public bool AutomationEnabled { get => automationEnabled; set { automationEnabled = value; Changed(); } }
    public TriggerChoice AutomationTrigger
    {
        get => automationTrigger ?? AutomationTriggers[0];
        set { automationTrigger = value; Changed(); Changed(nameof(AutomationUsesInterval)); }
    }
    public bool AutomationUsesInterval => AutomationTrigger.Id == RadioTrigger.Interval;
    public ActionChoice? ActionToAdd { get; set; }
    public ActionChoice? SelectedAction { get; set; }
    public string AutomationStatus { get => automationStatus; private set { automationStatus = value; Changed(); } }
    public ICommand NewAutomationCommand { get; private set; } = null!;
    public ICommand RaceStartTemplateCommand { get; private set; } = null!;
    public ICommand IntervalTemplateCommand { get; private set; } = null!;
    public ICommand SaveAutomationCommand { get; private set; } = null!;
    public ICommand DeleteAutomationCommand { get; private set; } = null!;
    public ICommand AddAutomationActionCommand { get; private set; } = null!;
    public ICommand RemoveAutomationActionCommand { get; private set; } = null!;
    public ICommand MoveAutomationActionUpCommand { get; private set; } = null!;
    public ICommand MoveAutomationActionDownCommand { get; private set; } = null!;
    public ICommand PreviewAutomationCommand { get; private set; } = null!;

    private void InitializeAutomation()
    {
        AsyncCommand Command(Action action) => GameCommand(() => { action(); return Task.CompletedTask; },
            error => AutomationStatus = error.Message);
        NewAutomationCommand = Command(() => NewAutomation(false));
        RaceStartTemplateCommand = Command(() => NewAutomation(true));
        IntervalTemplateCommand = Command(() =>
        {
            NewAutomation(false); AutomationName = "每两分钟报告赛况";
            ActionSequence.Add(AutomationActions.Single(a => a.Id == RadioAction.GapLeader));
            ActionSequence.Add(AutomationActions.Single(a => a.Id == RadioAction.GapAhead));
        });
        SaveAutomationCommand = Command(SaveAutomation);
        DeleteAutomationCommand = Command(() =>
        {
            if (SelectedAutomation == null) return;
            PersistAutomations(activeSettings.Automations.Where(r => r.Id != SelectedAutomation.Rule.Id).ToList());
            ReloadAutomations(); AutomationStatus = "已删除自动化。";
        });
        AddAutomationActionCommand = Command(() =>
        {
            if (ActionToAdd == null) return;
            if (ActionSequence.Count >= 12) throw new ArgumentException("每条自动化最多设置 12 个动作。");
            ActionSequence.Add(ActionToAdd);
        });
        RemoveAutomationActionCommand = Command(() => { if (SelectedAction != null) ActionSequence.Remove(SelectedAction); });
        MoveAutomationActionUpCommand = Command(() => MoveAction(-1));
        MoveAutomationActionDownCommand = Command(() => MoveAction(1));
        PreviewAutomationCommand = Command(() =>
        {
            var rule = EditedAutomation();
            AutomationStatus = radioFrame == null ? "请先连接游戏或启动演示数据。" :
                RadioAutomation.Render(rule, radioFrame, DateTimeOffset.UtcNow, raceReports, ImperialUnits);
        });
        ActionToAdd = AutomationActions[0];
        NewAutomation(false);
    }

    private void MoveAction(int direction)
    {
        if (SelectedAction == null) return;
        int index = ActionSequence.IndexOf(SelectedAction), destination = index + direction;
        if (index >= 0 && destination >= 0 && destination < ActionSequence.Count) ActionSequence.Move(index, destination);
    }

    private void NewAutomation(bool raceStart)
    {
        selectedAutomation = null; Changed(nameof(SelectedAutomation));
        LoadAutomation(new() { Game = SelectedGame.Id ?? Core.RacingGame.ACC,
            Name = raceStart ? "比赛开始报告排名" : "新的自动化",
            Trigger = raceStart ? RadioTrigger.RaceStart : RadioTrigger.Interval });
        AutomationStatus = "设置触发条件和动作，保存后生效。";
    }

    private void LoadAutomation(AutomationRule rule)
    {
        AutomationName = rule.Name; AutomationEnabled = rule.Enabled;
        AutomationInterval = rule.IntervalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        AutomationTrigger = AutomationTriggers.FirstOrDefault(t => t.Id == rule.Trigger) ?? AutomationTriggers[0];
        ActionSequence.Clear();
        foreach (var action in rule.Actions ?? [])
            if (AutomationActions.FirstOrDefault(a => a.Id == action) is { } choice) ActionSequence.Add(choice);
        SelectedAction = ActionSequence.FirstOrDefault(); Changed(nameof(SelectedAction));
    }

    private AutomationRule EditedAutomation()
    {
        if (!int.TryParse(AutomationInterval, out var seconds)) throw new ArgumentException("请填写整数秒数，例如 120 代表两分钟。");
        var rule = new AutomationRule { Id = SelectedAutomation?.Rule.Id ?? Guid.NewGuid(),
            Game = SelectedGame.Id ?? Core.RacingGame.ACC, Name = AutomationName.Trim(), Enabled = AutomationEnabled,
            Trigger = AutomationTrigger.Id, IntervalSeconds = seconds, Actions = ActionSequence.Select(a => a.Id).ToArray() };
        rule.Validate();
        return rule;
    }

    private void SaveAutomation()
    {
        var rule = EditedAutomation();
        var rules = activeSettings.Automations.Where(r => r.Id != rule.Id).ToList();
        if (rules.Count(r => r.Game == rule.Game) >= 40) throw new ArgumentException("每款游戏最多保存 40 条自动化。");
        rules.Add(rule); PersistAutomations(rules); ReloadAutomations();
        SelectedAutomation = Automations.Single(a => a.Rule.Id == rule.Id);
        AutomationStatus = $"已保存到 {GameName}，{(rule.Enabled ? "已启用" : "已停用")}。";
    }

    private void PersistAutomations(List<AutomationRule> rules)
    {
        var next = activeSettings with { Automations = rules };
        next.Save(settingsPath); activeSettings = next;
        automation.ClearPending();
    }

    private void ReloadAutomations()
    {
        Automations.Clear();
        foreach (var rule in activeSettings.Automations.Where(r => r.Game == SelectedGame.Id))
            Automations.Add(new(rule));
        NewAutomation(false);
    }
}
