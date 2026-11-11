using DeltaTR.Core;
using DeltaTR.Engineer;
using DeltaTR.Telemetry.ACC;
using DeltaTR.Telemetry.Abstractions;
using DeltaTR.Telemetry.Simulators;

namespace DeltaTR.App.ViewModels;

public sealed record GameChoice(RacingGame? Id, string Name);

public sealed partial class MainViewModel
{
    public IReadOnlyList<GameChoice> Games { get; } = [new(null, "请选择游戏"), new(RacingGame.ACC, "ACC"),
        new(RacingGame.LMU, "Le Mans Ultimate"), new(RacingGame.IRacing, "iRacing")];
    private GameChoice? currentGame;
    private bool switching;
    internal bool DemoOnFirstSelection { get; set; }
    public GameChoice SelectedGame
    {
        get => currentGame ?? Games[0];
        set { if (value is not null && value != SelectedGame && !switching) _ = SelectGameAsync(value); }
    }
    public bool HasGame => !switching && SelectedGame.Id.HasValue;
    public bool ShowGamePrompt => !HasGame;
    public bool CanSelectGame => !switching;
    public bool AccFeatures => HasGame && SelectedGame.Id == RacingGame.ACC;
    public bool NativeRaceFeatures => HasGame && SelectedGame.Id != RacingGame.ACC;
    public string NativeRaceStatus => isDemo && !stopped ? "演示赛事数据" :
        shown?.Native?.RaceStatus ?? $"等待 {GameName} 提供计时和排名数据";
    public string GameName => SelectedGame.Id?.Display() ?? "游戏";
    public string WheelDescription => SelectedGame.Id == RacingGame.IRacing
        ? "上次测量的中部胎体温度 / 冷胎压设定" : "核心温度 / 当前胎压";
    public string ExampleQuestions => SelectedGame.Id switch
    {
        RacingGame.IRacing => "现在车速多少\n还剩多少油\n排名\n左前冷胎压多少\n前车是谁 / 比赛排名表",
        RacingGame.LMU => "当前轮胎温度多少\n刹车温度多少\n前车差多少\n虚拟能量还剩多少\n前车是谁 / 比赛排名表",
        _ => "车辆状况 / 赛场状况\n现在胎压怎么样\n还剩多少油\n排名\n前车差多少 / 后车差多少\n刚才圈速 / 下雨吗"
    };

    internal async Task SelectGameAsync(GameChoice choice)
    {
        if (switching || closing || choice == SelectedGame) return;
        switching = true;
        try
        {
            currentGame = choice;
            if (choice.Id.HasValue) AlertGame = choice;
            currentCatalog = null;
            VocabularyFields = choice.Id is { } id ? RadioVocabulary.For(id) : [];
            NotifyGame();
            ReloadAutomations();
            LoadStrategy();
            CancelVoice();
            overlayWindow?.Close();
            await StopWheelScreensAsync();
            SetBindingMode(false);
            await StopAsync();
            drafts.Clear(); vocabularyFilter = ""; fieldFilter = "";
            SelectedPage = Navigation[0];
            Changed(nameof(VocabularyFilter)); Changed(nameof(FieldFilter));
            LastQuestion = "等待你的呼叫"; LastInput = "等待语音输入"; VocabularyStatus = "";
            Question = choice.Id == RacingGame.IRacing ? "现在车速多少" : "现在胎压怎么样";
            if (choice.Id is { } game)
            {
                try { queries = new TelemetryQueries(CurrentPhrases, activeSettings.WakeWord, game); }
                catch (ArgumentException e)
                {
                    queries = new TelemetryQueries(new Dictionary<string, string[]>(), "", game);
                    VocabularyStatus = "当前游戏词库加载失败，已使用默认问法：" + e.Message;
                }
                SelectedField = FilteredVocabulary[0].Field;
            }
            Notice = choice.Id is null ? "请在左侧选择游戏" : $"已选择 {GameName}";
            if (choice.Id.HasValue)
            {
                var demo = DemoOnFirstSelection;
                DemoOnFirstSelection = false;
                await StartAsync(demo, duringSwitch: true);
            }
        }
        catch (Exception e) { Notice = "游戏切换失败：" + e.Message; }
        finally { switching = false; NotifyGame(); }
    }

    private AsyncCommand GameCommand(Func<Task> action, Action<Exception> onError) => new(action, onError, () => HasGame);

    private void NotifyGame()
    {
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        Changed(nameof(CanTestMicrophone));
        foreach (var name in new[] { nameof(SelectedGame), nameof(HasGame), nameof(ShowGamePrompt), nameof(CanSelectGame),
            nameof(AccFeatures), nameof(NativeRaceFeatures), nameof(NativeRaceStatus), nameof(GameName), nameof(ExampleQuestions), nameof(WheelDescription), nameof(CanTalk),
            nameof(ConnectLabel), nameof(SourceLabel), nameof(VocabularyFields), nameof(CatalogSummary), nameof(FilteredVocabulary), nameof(PhraseEditor) })
            Changed(name);
    }

    private Dictionary<string, string[]> CurrentPhrases => SelectedGame.Id is RacingGame.LMU or RacingGame.IRacing
        ? activeSettings.GamePhrases.GetValueOrDefault(GameName) ?? new() : activeSettings.PhraseOverrides;

    private IGameAdapter CreateAdapter(bool demo, RacingGame game) => demo ? new SyntheticAdapter(game) : game switch
    {
        RacingGame.LMU => new LmuAdapter(),
        RacingGame.IRacing => new IRacingAdapter(),
        _ => new AccAdapter(calibration)
    };
}
