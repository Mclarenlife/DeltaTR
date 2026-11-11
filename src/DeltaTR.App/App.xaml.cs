using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeltaTR.App.ViewModels;
using DeltaTR.App.Input;

using DeltaTR.App.Diagnostics;
using System.Diagnostics;
using System.Windows.Controls;

namespace DeltaTR.App;

public partial class App : Application
{
    private void ApplyTheme(bool light)
    {
        var palettes = Resources.MergedDictionaries;
        var source = new Uri(light ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative);
        if (palettes[0].Source != source)
            palettes[0] = new ResourceDictionary { Source = source };
    }
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var smoke = Array.IndexOf(e.Args, "--smoke-test");
        TextWriterTraceListener? bindingLog = null;
        if (smoke >= 0 && smoke + 1 < e.Args.Length)
        {
            DispatcherUnhandledException += (_, args) =>
            {
                File.WriteAllText(Path.ChangeExtension(e.Args[smoke + 1], ".error.log"), args.Exception.ToString());
                args.Handled = true;
                Shutdown(1);
            };
            bindingLog = new TextWriterTraceListener(Path.ChangeExtension(e.Args[smoke + 1], ".bindings.log"));
            PresentationTraceSources.DataBindingSource.Listeners.Add(bindingLog);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        }
        var window = new MainWindow();
        var smokeFolder = smoke >= 0 && smoke + 1 < e.Args.Length ? Path.GetDirectoryName(Path.GetFullPath(e.Args[smoke + 1])) : null;
        var testApi = smokeFolder is null ? null : new SmokeChatHandler();
        var testClient = testApi is null ? null : new System.Net.Http.HttpClient(testApi);
        var testSettings = smokeFolder is null ? null : Path.Combine(smokeFolder, Guid.NewGuid().ToString("N") + ".settings.json");
        var viewModel = new MainViewModel(testSettings, testClient);
        ApplyTheme(viewModel.LightTheme);
        viewModel.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(viewModel.LightTheme)) ApplyTheme(viewModel.LightTheme);
        };
        window.DataContext = viewModel;
        PushToTalk? ptt = smoke < 0 ? new PushToTalk(viewModel) : null;
        MainWindow = window;
        window.Loaded += async (_, _) =>
        {
            viewModel.DemoOnFirstSelection = e.Args.Contains("--demo") || smoke >= 0;
            if (smoke >= 0 && smoke + 1 < e.Args.Length)
            {
                if (viewModel.HasGame || viewModel.CanTalk || viewModel.LiveCommand.CanExecute(null) || viewModel.ToggleOverlayCommand.CanExecute(null) || window.GameWorkspace.IsVisible)
                    throw new InvalidOperationException("Startup must wait for a game");
                await viewModel.SelectGameAsync(viewModel.Games.Single(g => g.Id == Core.RacingGame.ACC));
                await Task.Delay(1500);
                viewModel.SpeakReplies = false;
                viewModel.QueryCommand.Execute(null);
                if (e.Args.Contains("--smoke-wave"))
                    await WaveSmoke.RunAsync(smokeFolder!);
                if (e.Args.Contains("--smoke-dashboard-switch"))
                    await DashboardSwitchSmoke.RunAsync(viewModel, smokeFolder!);
                var wheelProbe = Array.IndexOf(e.Args, "--smoke-wheel");
                if (wheelProbe >= 0 && wheelProbe + 1 < e.Args.Length)
                    await WheelSmoke.RunAsync(viewModel, e.Args[wheelProbe + 1], smokeFolder!);
                if (e.Args.Contains("--smoke-sensevoice"))
                    await SpeechBenchmark.RunAsync(smokeFolder!);
                if (e.Args.Contains("--smoke-voice"))
                    await VoiceSmoke.RunAsync(Path.GetDirectoryName(Path.GetFullPath(e.Args[smoke + 1]))!);
                if (e.Args.Contains("--smoke-shell"))
                    await ShellSmoke.RunAsync(window, viewModel, Path.GetDirectoryName(Path.GetFullPath(e.Args[smoke + 1]))!, RenderAsync, testApi!, testSettings!);
                await RenderAsync(window, e.Args[smoke + 1]);
                bindingLog?.Flush();
                // Native SC_CLOSE can be ignored after popup focus on a hidden test desktop.
                // Close the harness directly so temporary settings and native models are always disposed.
                window.Close();
            }
        };
        var closing = false;
        window.Closing += async (_, args) =>
        {
            if (closing) return;
            args.Cancel = true;
            closing = true;
            ptt?.Dispose();
            await viewModel.ShutdownAsync();
            testClient?.Dispose();
            if (testSettings is not null) File.Delete(testSettings);
            // Shutdown may finish synchronously. Re-enter Close only after this Closing event unwinds.
            _ = window.Dispatcher.BeginInvoke(window.Close);
        };
        window.Show();
    }

    private static async Task RenderAsync(Window window, string path)
    {
        await window.Dispatcher.InvokeAsync(() => window.UpdateLayout(), DispatcherPriority.Render);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var drawing = backdrop.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight));
        bitmap.Render(backdrop);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
}
