using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeltaTR.App.ViewModels;

namespace DeltaTR.App.Diagnostics;

/// <summary>Opt-in integration smoke: exercise actual bindings, page views and native window commands.</summary>
internal static class ShellSmoke
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    public static async Task RunAsync(MainWindow window, MainViewModel vm, string folder, Func<Window, string, Task> render, SmokeChatHandler api, string settingsFile)
    {
        async Task WaitForState(WindowState expected)
        {
            for (var i = 0; i < 40 && window.WindowState != expected; i++)
                await Task.Delay(50);
        }
        var checks = new List<string>();
        void Require(bool condition, string check)
        {
            if (!condition) throw new InvalidOperationException("Shell smoke failed: " + check);
            checks.Add("PASS " + check);
        }
        Require(vm.UsePitHouseBridge && vm.WheelConnectionModes.Count == 2 && vm.CanChangeWheelMode,
            "Wheel display defaults to opt-in Pit House bridge with explicit independent alternative");
        vm.UsePitHouseBridge = false;
        Require(vm.WheelConnectionHelp.Contains("串口"), "Independent wheel mode explains serial ownership");
        vm.UsePitHouseBridge = true;
        Require(vm.WheelConnectionHelp.Contains("专用文字字段") && vm.WheelConnectionHelp.Contains("Wave Radio") && vm.WheelButtonText.Contains("开启"),
            "Dedicated field upgrade guidance is visible and display does not start automatically");
        vm.WheelSmartSwitch = false; vm.WheelAutoPopup = false;
        Require(!Settings.RadioSettings.Load(settingsFile).WheelSmartSwitch && !Settings.RadioSettings.Load(settingsFile).WheelAutoPopup,
            "Wheel smart switching and automatic popup preferences persist");
        vm.WheelSmartSwitch = true; vm.WheelAutoPopup = true;
        Require(new Settings.RadioSettings().EffectiveEngine == Voice.SpeechEngine.Windows &&
            System.Text.Json.JsonSerializer.Deserialize<Settings.RadioSettings>("{\"UseZipformer\":true}")!.EffectiveEngine == Voice.SpeechEngine.Zipformer,
            "Legacy engine settings retain Windows or Zipformer");
        foreach (var page in vm.Navigation)
        {
            window.NavigationList.SelectedItem = page;
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
            Require(vm.SelectedPage == page, "Navigation binding: " + page.Key);
            var expected = page.Key switch { "overview" => "OverviewView", "radio" => "RadioView", "overlay" => "OverlayView", "vocabulary" => "VocabularyView", "automation" => "AutomationView", "strategy" => "StrategyView", _ => "SettingsView" };
            Require(FindVisual<UserControl>(window)?.GetType().Name == expected, "Content view: " + page.Key);
            await render(window, Path.Combine(folder, "page-" + page.Key + ".png"));
        }
        vm.SelectedPage = vm.Navigation.Single(page => page.Key == "vocabulary");
        vm.VocabularyFilter = "刹车温度";
        Require(vm.FilteredVocabulary.Count == 5, "Search finds aggregate and four individual brake temperatures");
        vm.VocabularyFilter = "";
        await vm.SubmitQuestionAsync("刹车温度多少");
        Require(vm.Answer.Contains("左前、右前、左后、右后") && vm.Answer.Contains("°C"), "Brake temperatures reach the radio");
        checks.Add($"Radio vocabulary: {vm.VocabularyFields.Count} selectable queries");
        vm.SelectedField = vm.VocabularyFields.Single(f => f.Key == "airTemperature");
        vm.PhraseEditor = "报告气温\n气温";
        vm.SaveVocabularyCommand.Execute(null);
        Require(Settings.RadioSettings.Load(settingsFile).PhraseOverrides["airTemperature"].Contains("报告气温"), "Vocabulary aliases persist");
        await vm.SubmitQuestionAsync("报告气温");
        Require(vm.Answer.Contains("气温") && vm.Answer.Contains("【演示数据】"), "Custom alias queries the selected metric");
        vm.PhraseEditor = "油量";
        vm.SaveVocabularyCommand.Execute(null);
        Require(vm.VocabularyStatus.StartsWith("未保存"), "Conflicting alias rejected");
        vm.ResetFieldCommand.Execute(null);
        vm.SaveVocabularyCommand.Execute(null);
        Require(!Settings.RadioSettings.Load(settingsFile).PhraseOverrides.ContainsKey("airTemperature"), "Restore built-in aliases");
        vm.SelectedPage = vm.Navigation.Single(page => page.Key == "overlay");
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
        await vm.StartAsync(true);
        await Task.Delay(350);
        vm.ToggleOverlayCommand.Execute(null);
        var overlay = Application.Current.Windows.OfType<RadioOverlay>().Single();
        await window.Dispatcher.InvokeAsync(overlay.UpdateLayout, DispatcherPriority.ContextIdle);
        Require(!vm.BubbleVisible && overlay.IsVisible && overlay.Surface.Avatar.IsVisible &&
            overlay.Surface.Bubble.Visibility == Visibility.Collapsed, "Connected idle shows only the avatar");
        await vm.StopAsync();
        Require(vm.BubbleVisible && vm.BubbleText == "未连接", "Disconnected bubble");
        await vm.StartAsync(true);
        await Task.Delay(350);
        Require(!vm.BubbleVisible, "Reconnect clears the disconnected bubble");
        vm.Question = "轮胎温度多少";
        Require(vm.OverlayStatus.Contains("正在输入") && vm.BubbleText == vm.Question, "Overlay shows draft while typing");
        Require(overlay.IsVisible && overlay.Topmost && !overlay.ShowActivated, "Independent topmost conversation window");
        var origin = overlay.PointToScreen(new Point(0, 0));
        Require(GetWindowRect(new WindowInteropHelper(overlay).Handle, out var bounds) && Math.Abs(origin.Y - bounds.Top) <= 1,
            "Overlay content begins at the native top edge");
        var pageView = FindVisual<Views.OverlayView>(window)!;
        pageView.AvatarSizeSlider.Value = 125;
        pageView.BubbleSizeSlider.Value = 80;
        pageView.TextSlider.Value = 72;
        pageView.AvatarSlider.Value = 85;
        pageView.BackdropSlider.Value = 30;
        foreach (var layout in vm.Placements)
        {
            vm.TextPlacement = layout.Key;
            await window.Dispatcher.InvokeAsync(overlay.UpdateLayout, DispatcherPriority.ContextIdle);
            foreach (var surface in new[] { overlay.Surface, pageView.Preview })
            {
                Require(Grid.GetColumn(surface.Bubble) == (layout.Key == "Right" ? 1 : 0) &&
                    Grid.GetRow(surface.Bubble) == (layout.Key == "Bottom" ? 1 : 0) &&
                    Grid.GetColumn(surface.Avatar) == (layout.Key == "Left" ? 1 : 0) &&
                    Grid.GetRow(surface.Avatar) == (layout.Key == "Top" ? 1 : 0), "Text placement " + layout.Key);
                Require(surface.Avatar.Width == 120 && surface.Bubble.LayoutTransform.Value.M11 == .8, "Independent avatar and bubble scales");
                Require(Math.Abs(surface.Transcript.Opacity - .72) < .001 && Math.Abs(surface.Avatar.Opacity - .85) < .001 &&
                    Math.Abs(surface.Backdrop.Opacity - .30) < .001, "Three independent opacity bindings");
            }
        }
        vm.TextPlacement = "Right";
        var avatar = overlay.Surface.Avatar;
        var idleFrame = CaptureAvatar(avatar);
        avatar.DataContext = new { IsListening = true, AudioLevel = 88, AvatarAlpha = .85, AvatarSide = 120d };
        await Task.Delay(320);
        Require(avatar.Listening && avatar.Level == 88 && !idleFrame.SequenceEqual(CaptureAvatar(avatar)),
            "Crystal renders different geometry with simulated held PTT and input level");
        await render(overlay, Path.Combine(folder, "overlay-listening.png"));
        avatar.ClearValue(FrameworkElement.DataContextProperty);
        await window.Dispatcher.InvokeAsync(overlay.UpdateLayout, DispatcherPriority.ContextIdle);
        Require(!avatar.Listening && avatar.Level == 0, "Crystal returns to live idle state");
        await vm.SubmitQuestionAsync("工程师，我应该第几圈进站");
        Require(vm.VoiceStatus == "AI 尚未配置", "Wake request without API gives setup guidance");
        await vm.SubmitQuestionAsync("轮胎温度多少");
        Require(vm.Answer.Contains("胎温") && vm.Answer.Contains("演示数据"), "Ordinary question uses local telemetry");
        await render(overlay, Path.Combine(folder, "overlay-window.png"));
        Require(FindVisual<TextBlock>(overlay) is not null && overlay.DataContext == vm, "Overlay shares live radio state");
        await Task.Delay(TimeSpan.FromSeconds(8.5));
        Require(!vm.BubbleVisible && overlay.IsVisible && overlay.Surface.Avatar.IsVisible &&
            overlay.Surface.Bubble.Visibility == Visibility.Collapsed, "Reply expires while the avatar stays visible");
        await render(overlay, Path.Combine(folder, "overlay-idle.png"));
        await vm.SubmitQuestionAsync("左前胎温");
        var firstReading = vm.Answer;
        for (var i = 0; i < 20 && vm.Answer == firstReading; i++) await Task.Delay(100);
        Require(vm.Answer != firstReading && vm.Answer.Contains("左前胎温"), "Tyre reading follows new samples without another question");
        await vm.SubmitQuestionAsync("左前胎温");
        Require(vm.Answer != firstReading && vm.BubbleText == vm.Answer, "Repeated tyre question reads the current sample");
        overlay.Close();
        Require(vm.OverlayButtonText == "开启屏显", "Closing overlay resets switch");
        vm.SelectedPage = vm.Navigation.Single(p => p.Key == "settings");
        var style = Settings.RadioSettings.Load(settingsFile);
        Require(style.TextOpacity == 72 && style.AvatarOpacity == 85 && style.BackdropOpacity == 30 && style.TextPlacement == "Right" && style.AvatarSizePercent == 125 && style.BubbleSizePercent == 80, "Overlay style persists");
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
        if (Environment.GetCommandLineArgs().Contains("--smoke-mic-level"))
        {
            var samples = 0;
            void CountLevel(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
            {
                if (args.PropertyName == nameof(vm.AudioLevel)) samples++;
            }
            vm.PropertyChanged += CountLevel;
            try
            {
                vm.TestMicrophoneCommand.Execute(null);
                for (var i = 0; i < 20 && samples < 3; i++) await Task.Delay(50);
                Require(vm.TestMicrophoneLabel == "停止测试" && samples >= 3 && !vm.IsListening, "Selected microphone produces meter samples without PTT");
            }
            finally
            {
                vm.PropertyChanged -= CountLevel;
                if (vm.TestMicrophoneLabel == "停止测试") vm.TestMicrophoneCommand.Execute(null);
            }
            Require(vm.AudioLevel == 0 && vm.TestMicrophoneLabel == "测试麦克风", "Meter stops and clears");
        }
        var settingsView = FindVisual<Views.SettingsView>(window)!;
        async Task CheckInputsAsync(Views.SettingsView controlsView)
        {
            var picker = controlsView.AlertGameSelector;
            var originalSelection = picker.SelectedIndex;
            picker.BringIntoView();
            picker.Focus();
            picker.ApplyTemplate();
            var toggle = (System.Windows.Controls.Primitives.ToggleButton)picker.Template.FindName("DropToggle", picker);
            toggle.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, true);
            await Task.Delay(200);
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
            Require(picker.IsDropDownOpen, "Themed dropdown opens from its toggle");
            var popup = (System.Windows.Controls.Primitives.Popup)picker.Template.FindName("PART_Popup", picker);
            var menu = (FrameworkElement)popup.Child;
            var popupBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),
                (int)Math.Ceiling(menu.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            popupBitmap.Render(menu);
            var popupEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            popupEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(popupBitmap));
            using (var menuImage = File.Create(Path.Combine(folder, "dropdown-open.png"))) popupEncoder.Save(menuImage);
            picker.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window), Environment.TickCount, System.Windows.Input.Key.Down)
                { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
            picker.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window), Environment.TickCount, System.Windows.Input.Key.Enter)
                { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
            Require(picker.SelectedIndex == originalSelection + 1, "Themed dropdown supports arrow-key selection and Enter commits");
            picker.SetCurrentValue(System.Windows.Controls.ComboBox.IsDropDownOpenProperty, true);
            await Task.Delay(200);
            picker.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window), Environment.TickCount, System.Windows.Input.Key.Escape)
                { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
            Require(!picker.IsDropDownOpen, "Escape dismisses the themed dropdown");
            await Task.Delay(200);
            Require(System.Windows.Input.Mouse.Captured == null, "Closing dropdown releases mouse capture");
            picker.SetCurrentValue(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty, originalSelection);
            var originalThreshold = vm.InputThreshold;
            controlsView.InputThresholdSlider.BringIntoView();
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
            controlsView.InputThresholdSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, 20d);
            var sliderTrack = (System.Windows.Controls.Primitives.Track)controlsView.InputThresholdSlider.Template.FindName("PART_Track", controlsView.InputThresholdSlider);
            sliderTrack.Thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(30, 0));
            Require(vm.InputThreshold > 20, "Themed slider drag updates the bound microphone threshold");
            await render(window, Path.Combine(folder, "settings-sliders.png"));
            controlsView.InputThresholdSlider.SetCurrentValue(System.Windows.Controls.Primitives.RangeBase.ValueProperty, (double)originalThreshold);
        }
        settingsView.AlertGameSelector.SelectedItem = vm.Games.Single(g => g.Id == Core.RacingGame.LMU);
        settingsView.YellowAlertChoice.IsChecked = false;
        var alerts = Settings.RadioSettings.Load(settingsFile).RaceAlerts;
        Require(!alerts["LMU"].HasFlag(Engineer.RaceAlertKind.Yellow) && vm.SelectedGame.Id == Core.RacingGame.ACC,
            "Race alert checkboxes persist for another game without changing the connected simulator");
        settingsView.AlertGameSelector.SelectedItem = vm.SelectedGame;
        Require(settingsView.YellowAlertChoice.IsChecked == true, "Current game's yellow flag choice stays enabled");
        // Hold a sample constant so that only dragging the settings slider can change the gate.
        settingsView.SenseVoiceEngineChoice.IsChecked = true;
        Require(vm.UseSenseVoice && vm.CanTalk && !vm.UseWindowsRecognition && !vm.UseZipformer &&
            Settings.RadioSettings.Load(settingsFile).EffectiveEngine == Voice.SpeechEngine.SenseVoice,
            "SenseVoice selection activates and persists");
        settingsView.SenseVoiceEngineChoice.BringIntoView();
        await render(window, Path.Combine(folder, "sensevoice-settings.png"));
        settingsView.ZipformerEngineChoice.IsChecked = true;
        Require(vm.UseZipformer && vm.CanTalk && Settings.RadioSettings.Load(settingsFile).UseZipformer, "Zipformer selection activates and persists");
        settingsView.WindowsEngineChoice.IsChecked = true;
        Require(vm.UseWindowsRecognition && !Settings.RadioSettings.Load(settingsFile).UseZipformer, "Switch back to Windows recognition");
        var previousThreshold = vm.InputThreshold;
        vm.AudioLevel = 11.75;
        settingsView.InputThresholdSlider.Value = 12;
        Require(settingsView.ThresholdLine.Threshold == 12 && settingsView.AcceptedInputMeter.Value == 0 &&
            settingsView.RawInputMeter.Value == 11.75, "Input below the threshold stays visible as raw audio only");
        settingsView.InputThresholdSlider.Value = 11;
        Require(settingsView.ThresholdLine.Threshold == 11 && settingsView.AcceptedInputMeter.Value == 11.75 &&
            vm.InputThreshold == 11, "Dragging the threshold accepts the same sample immediately");
        vm.AudioLevel = 0;
        settingsView.InputThresholdSlider.Value = previousThreshold;
        settingsView.ImperialChoice.IsChecked = true;
        Require(Settings.RadioSettings.Load(settingsFile).ImperialUnits && vm.Speed.Contains("mph") &&
            vm.Wheels.All(w => w.Values.Contains("°F") && w.Values.Contains("psi")), "Imperial switch refreshes dashboard and persists");
        await vm.SubmitQuestionAsync("气温");
        Require(vm.Answer.Contains("75.2 °F"), "Radio temperature in Fahrenheit");
        settingsView.MetricChoice.IsChecked = true;
        Require(vm.Answer.Contains("24.0 °C") && vm.Wheels.All(w => w.Values.Contains("psi")) &&
            !Settings.RadioSettings.Load(settingsFile).ImperialUnits, "Metric switch updates local reply and tyre readings");
        settingsView.BindTalkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        using (var controllers = new Input.WheelButtons())
        {
            var hardware = controllers.Read(new WindowInteropHelper(window).Handle);
            checks.Add("DirectInput read: " + controllers.Status + " (" + hardware.Count + ")");
        }
        var wheelId = Guid.NewGuid();
        var releasedWheel = new Input.WheelState(wheelId, "测试方向盘", new bool[128]);
        var pressedWheel = releasedWheel with { Buttons = new bool[128] };
        pressedWheel.Buttons[63] = true;
        Require(Input.PushToTalk.FindPressedButton([], [pressedWheel]) is null, "Held button on attach does not bind");
        var wheelBinding = Input.PushToTalk.FindPressedButton([releasedWheel], [pressedWheel])!;
        vm.AcceptBinding(wheelBinding);
        Require(Settings.RadioSettings.Load(settingsFile).PushToTalkBinding == wheelBinding &&
            vm.BoundDeviceLabel == "测试方向盘 · 按钮 64", "Wheel device ID and button persist");
        var held = new Input.HoldButton();
        Require(new bool?[] { false, true, true, false, true, null, true }.Select(held.Update).SequenceEqual(
            [Input.TalkEdge.None, Input.TalkEdge.Press, Input.TalkEdge.None, Input.TalkEdge.Release, Input.TalkEdge.Press, Input.TalkEdge.Lost, Input.TalkEdge.None]), "Hold, release, unplug and rearm");
        settingsView.BindTalkButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var keyPress = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
            PresentationSource.FromVisual(settingsView), Environment.TickCount, System.Windows.Input.Key.F9)
        { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
        settingsView.BindTalkButton.RaiseEvent(keyPress);
        Require(Settings.RadioSettings.Load(settingsFile).PushToTalkBinding == Input.TalkBinding.Keyboard(0x78) && vm.TalkLabel.Contains("F9"), "Selected talk key applies and persists");
        FindVisual<Expander>(window)!.IsExpanded = true;
        vm.FieldFilter = "pressure."; vm.CompactTables = true;
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
        var table = FindVisual<DataGrid>(window)!;
        Require(table.Items.Count == 4 && table.RowHeight == 30, "Field filter + compact preference in actual DataGrid");
        vm.FieldFilter = "no-such-field";
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
        Require(table.Items.Count == 0, "Empty field filter");
        vm.FieldFilter = ""; vm.CompactTables = false;
        vm.ApiAddress = "http://localhost:8080/v1";
        vm.ApiModel = "smoke-model";
        vm.ApiKeyEdit = "synthetic-smoke-key";
        Require(!vm.EnhanceVoice && !vm.ReduceNoise, "Existing settings leave microphone effects off");
        settingsView.ReduceNoiseChoice.IsChecked = true;
        Require(vm.ReduceNoise && !vm.EnhanceVoice, "Noise suppression can be used independently");
        settingsView.EnhanceVoiceChoice.IsChecked = true;
        vm.WakeWordEdit = "队长";
        vm.SaveSettingsCommand.Execute(null);
        var saved = Settings.RadioSettings.Load(settingsFile);
        Require(saved.EnhanceVoice && saved.ReduceNoise, "Microphone effects persist");
        Require(saved.WakeWord == "队长" && saved.Decrypt() == vm.ApiKeyEdit &&
            !File.ReadAllText(settingsFile).Contains(vm.ApiKeyEdit), "Settings and user-encrypted key round trip");
        await vm.SubmitQuestionAsync("轮胎温度多少");
        Require(api.Requests == 0 && vm.Answer.Contains("胎温"), "Telemetry bypasses configured API");
        await vm.SubmitQuestionAsync("工程师，我应该第几圈进站");
        Require(api.Requests == 0, "Former wake word cannot send to API");
        await vm.SubmitQuestionAsync("队长，我应该第几圈进站");
        Require(api.Requests == 1 && vm.Answer.Contains("每圈油耗") && vm.Answer.StartsWith("【演示数据】"), "Custom wake routes to API and labels reply");
        await vm.SubmitQuestionAsync("队长，为什么");
        Require(api.Requests == 2 && api.LastBody.Contains("assistant"), "AI follow-up includes prior conversation");
        vm.SelectedPage = vm.Navigation.Single(p => p.Key == "strategy");
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
        var strategyView = FindVisual<Views.StrategyView>(window)!;
        strategyView.LengthSelector.SelectedItem = vm.StrategyLengths.Single(p => p.Id == Engineer.ReplyLength.Detailed);
        strategyView.ApproachSelector.SelectedItem = vm.StrategyApproaches.Single(p => p.Id == Engineer.StrategyApproach.Conservative);
        strategyView.InstructionsEditor.SetCurrentValue(TextBox.TextProperty, "先给结论，称呼我为车手。");
        strategyView.InstructionsEditor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        strategyView.TyreChoice.IsChecked = false;
        strategyView.SaveButton.Command.Execute(null);
        var strategy = Settings.RadioSettings.Load(settingsFile).Strategies["ACC"];
        Require(strategy.ReplyLength == Engineer.ReplyLength.Detailed && strategy.Approach == Engineer.StrategyApproach.Conservative &&
            !strategy.ShowTyres && strategy.Instructions.Contains("先给结论"), "Strategy preferences and display choices persist");
        Require(!strategyView.TyreCard.IsVisible && strategyView.PitCard.IsVisible, "Strategy display selection controls cards");
        var beforeStrategy = api.Requests;
        vm.PitAnalysis.RunCommand.Execute(null);
        for (var i = 0; i < 100 && vm.PitAnalysis.Result.Contains("正在分析"); i++) await Task.Delay(20);
        Require(api.Requests == beforeStrategy + 1 && vm.PitAnalysis.Result.Contains("每圈油耗") &&
            vm.PitAnalysis.UpdatedText.Contains("ACC"), "Pit analysis uses existing AI route and shows a timestamped result");
        using (var body = System.Text.Json.JsonDocument.Parse(api.LastBody))
        {
            var messages = body.RootElement.GetProperty("messages");
            Require(messages[0].GetProperty("content").GetString()!.Contains("优先完赛") &&
                messages[messages.GetArrayLength() - 1].GetProperty("content").GetString()!.Contains("先给结论"),
                "Saved conversation preferences reach the AI request");
        }
        await render(window, Path.Combine(folder, "strategy-analysis.png"));
        await vm.SubmitQuestionAsync("轮胎温度多少");
        var originalWidth = window.Width; var originalHeight = window.Height;
        window.Width = 1080; window.Height = 720;
        foreach (var page in vm.Navigation)
        {
            window.NavigationList.SelectedItem = page;
            await render(window, Path.Combine(folder, "compact-" + page.Key + ".png"));
        }
        window.Width = originalWidth; window.Height = originalHeight;
        window.MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForState(WindowState.Maximized);
        Require(window.WindowState == WindowState.Maximized, "Caption maximize command (state: " + window.WindowState + ", enabled: " + window.IsEnabled + ", captured: " + System.Windows.Input.Mouse.Captured + ")");
        await render(window, Path.Combine(folder, "maximized.png"));
        window.MaximizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForState(WindowState.Normal);
        Require(window.WindowState == WindowState.Normal, "Caption restore command");
        window.MinimizeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForState(WindowState.Minimized);
        Require(window.WindowState == WindowState.Minimized, "Caption minimize command");
        SystemCommands.RestoreWindow(window);
        await Task.Delay(120);
        // Exercise native caption commands before popup focus on a non-interactive test desktop.
        vm.SelectedPage = vm.Navigation.Single(page => page.Key == "settings");
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
        await CheckInputsAsync(FindVisual<Views.SettingsView>(window)!);
        vm.SelectedPage = vm.Navigation[0];
        vm.LiveCommand.Execute(null);
        await Task.Delay(1100);
        Require(vm.ConnectLabel == "重新检测 ACC" && vm.Notice == vm.SourceDescription, "Connect action switches from demo and reports actual state");
        checks.Add(vm.SourceLabel + " · " + vm.SourceDescription);
        if (Environment.GetCommandLineArgs().Contains("--smoke-live-tyres"))
        {
            for (var sample = 0; sample < 3; sample++)
            {
                await vm.SubmitQuestionAsync("胎压");
                checks.Add($"{vm.RadioAnswerLabel} · {vm.Answer}");
                await vm.SubmitQuestionAsync("胎温");
                checks.Add($"{vm.RadioAnswerLabel} · {vm.Answer}");
                if (sample < 2) await Task.Delay(2500);
            }
        }
        var acc = vm.Games.Single(g => g.Id == Core.RacingGame.ACC);
        foreach (var game in new[] { Core.RacingGame.LMU, Core.RacingGame.IRacing })
        {
            window.GamePicker.SelectedItem = vm.Games.Single(g => g.Id == game);
            for (var i = 0; i < 100 && (!vm.HasGame || vm.SelectedGame.Id != game); i++) await Task.Delay(20);
            Require(vm.HasGame && vm.SelectedGame.Id == game && !vm.AccFeatures, "Sidebar activates " + game);
            Require(vm.VocabularyFields.All(f => !f.Key.StartsWith("UDP.")), "ACC broadcast vocabulary hidden in " + game);
            await vm.StartAsync(true); await Task.Delay(350);
            await vm.SubmitQuestionAsync(game == Core.RacingGame.LMU ? "胎压" : "左前冷胎压多少");
            Require(vm.Answer.Contains("psi") && vm.Answer.Contains("演示数据"), "Per-game tyre query in " + game);
            await vm.SubmitQuestionAsync("前车是谁");
            Require(vm.Answer.Contains("示例前车车手") && vm.Answer.Contains("演示数据"), "Local race reports in " + game);
            checks.Add($"{game} vocabulary: {vm.VocabularyFields.Count} query items");
            await render(window, Path.Combine(folder, $"radio-{game}.png"));
            vm.SelectedField = vm.VocabularyFields.Single(f => f.Key == "fuel");
            vm.PhraseEditor = game == Core.RacingGame.LMU ? "勒芒剩油" : "爱赛车剩油";
            vm.SaveVocabularyCommand.Execute(null);
            await vm.SubmitQuestionAsync(vm.PhraseEditor);
            Require(vm.Answer.Contains("燃油"), "Per-game phrase overrides apply in " + game);
            if (game == Core.RacingGame.IRacing)
            {
                vm.SelectedPage = vm.Navigation.Single(p => p.Key == "vocabulary");
                vm.VocabularyFilter = "Precipitation";
                Require(vm.FilteredVocabulary.Any(f => f.Key == "IR.Var.Precipitation"), "iRacing reference variable searchable by SDK name");
                vm.SelectedField = vm.VocabularyFields.Single(f => f.Key == "IR.Var.Precipitation");
                Require(vm.PhraseEditor.Contains("下雨吗") && vm.SelectedField.Detail.Contains("原始单位"),
                    "iRacing weather aliases and source metadata shown");
                await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
                await render(window, Path.Combine(folder, "vocabulary-IRacing.png"));
                vm.VocabularyFilter = "";
                vm.SelectedPage = vm.Navigation.Single(p => p.Key == "radio");
                var previousQuestion = vm.LastQuestion;
                for (int i = 0; i < 90 && !vm.HasRaceAlert; i++) await Task.Delay(200);
                Require(vm.LastRaceAlert.Contains("黄旗") && vm.BubbleText == vm.LastRaceAlert &&
                    vm.LastQuestion == previousQuestion && vm.Answer.Contains("燃油"),
                    "Simulated yellow reaches the radio and overlay without replacing the driver's query");
            }
        }
        await vm.SelectGameAsync(acc); await vm.StartAsync(true); await Task.Delay(300);
        await vm.SubmitQuestionAsync("勒芒剩油");
        Require(vm.Answer.Contains("未匹配"), "LMU custom phrases do not leak into ACC");
        vm.SelectedPage = vm.Navigation.Single(p => p.Key == "automation");
        await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);
        var automationView = FindVisual<Views.AutomationView>(window)!;
        automationView.StartTemplateButton.Command.Execute(null);
        automationView.SaveRuleButton.Command.Execute(null);
        Require(Settings.RadioSettings.Load(settingsFile).Automations.Any(r => r.Trigger == Engineer.RadioTrigger.RaceStart),
            "Race-start template saves with current-game binding");
        automationView.IntervalTemplateButton.Command.Execute(null);
        automationView.IntervalSeconds.SetCurrentValue(TextBox.TextProperty, "10");
        automationView.IntervalSeconds.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        automationView.SaveRuleButton.Command.Execute(null);
        var savedRule = Settings.RadioSettings.Load(settingsFile).Automations.Single(r => r.Trigger == Engineer.RadioTrigger.Interval);
        Require(savedRule.IntervalSeconds == 10 && savedRule.Actions.SequenceEqual(new[] {
            Engineer.RadioAction.Position, Engineer.RadioAction.GapLeader, Engineer.RadioAction.GapAhead }),
            "Interval template preserves action order and edited seconds");
        vm.SelectedAction = vm.ActionSequence[2];
        vm.MoveAutomationActionUpCommand.Execute(null);
        Require(vm.ActionSequence[1].Id == Engineer.RadioAction.GapAhead, "Action reorder");
        vm.SelectedAction = vm.ActionSequence[1];
        vm.MoveAutomationActionDownCommand.Execute(null);
        vm.SaveAutomationCommand.Execute(null);
        await vm.StartAsync(true);
        for (int i = 0; i < 65 && !vm.AutomationStatus.Contains("头车"); i++) await Task.Delay(200);
        Require(vm.AutomationStatus.Contains("上次执行") && vm.LastRaceAlert.Contains("12.4 秒") &&
            vm.LastRaceAlert.Contains("1.8 秒"), "Periodic automation reaches the normal radio and overlay pipeline");
        await render(window, Path.Combine(folder, "automation-running.png"));
        vm.AutomationEnabled = false; vm.SaveAutomationCommand.Execute(null);
        Require(Settings.RadioSettings.Load(settingsFile).Automations.Single(r => r.Id == savedRule.Id).Enabled == false,
            "Disable automation persists");
        await vm.SelectGameAsync(vm.Games.Single(g => g.Id == Core.RacingGame.LMU));
        Require(vm.Automations.Count == 0, "Automations do not leak across games");
        Require(vm.StrategyInstructions == "" && vm.ShowTyreAnalysis && vm.PitAnalysis.UpdatedText == "",
            "Strategy preferences and results are isolated by game");
        await vm.SelectGameAsync(acc);
        Require(vm.StrategyInstructions.Contains("先给结论") && !vm.ShowTyreAnalysis,
            "Strategy preferences restore when returning to the game");
        await vm.SelectGameAsync(vm.Games[0]);
        Require(!vm.HasGame && !vm.CanTalk && !window.GameWorkspace.IsVisible && !vm.DemoCommand.CanExecute(null),
            "Clearing the game locks the workspace and PTT");
        await render(window, Path.Combine(folder, "choose-game.png"));
        await vm.SelectGameAsync(acc);
        await File.WriteAllLinesAsync(Path.Combine(folder, "shell-smoke.log"), checks);
    }

    private static byte[] CaptureAvatar(Views.IceCrystal avatar)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)avatar.ActualWidth, (int)avatar.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        avatar.UpdateLayout();
        var bounds = new Rect(0, 0, avatar.ActualWidth, avatar.ActualHeight);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            context.DrawRectangle(new VisualBrush(avatar) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = bounds }, null, bounds);
        bitmap.Render(visual);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }

    private static T? FindVisual<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var nested = FindVisual<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }
}
