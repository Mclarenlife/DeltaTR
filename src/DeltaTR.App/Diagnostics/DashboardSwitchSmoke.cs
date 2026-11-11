using System.IO;
using DeltaTR.App.ViewModels;

namespace DeltaTR.App.Diagnostics;

/// <summary>Explicit hardware check using isolated app settings, no microphone and no vehicle commands.</summary>
internal static class DashboardSwitchSmoke
{
    public static async Task RunAsync(MainViewModel vm, string folder)
    {
        string log = Path.Combine(folder, "dashboard-switch.log");
        void Record(string text) => File.AppendAllText(log, DateTimeOffset.Now.ToString("O") + " " + text + Environment.NewLine);
        async Task Expect(string text, int seconds = 12)
        {
            var until = DateTime.UtcNow.AddSeconds(seconds);
            while (!vm.WheelStatus.Contains(text, StringComparison.Ordinal))
            {
                if (DateTime.UtcNow > until) throw new IOException("Dashboard check: " + text + "; " + vm.WheelStatus);
                await Task.Delay(200);
            }
            Record(vm.WheelStatus);
        }
        vm.SelectedPage = vm.Navigation.Single(p => p.Key == "overlay");
        vm.UsePitHouseBridge = true; vm.WheelSmartSwitch = true; vm.WheelAutoPopup = false;
        vm.ToggleWheelScreenCommand.Execute(null);
        try
        {
            await Expect("比赛仪表"); await Task.Delay(4000);
            vm.ToggleWheelPresentation(); await Expect("DeltaTR 画面");
            await vm.SubmitQuestionAsync("当前轮胎温度多少"); await Task.Delay(8000);
            vm.ToggleWheelPresentation(); await Expect("比赛仪表"); await Task.Delay(4000);
            vm.WheelAutoPopup = true;
            vm.PresentRaceMessage("【屏幕切换测试】前方黄旗，请减速。此消息为验证内容。");
            await Expect("DeltaTR 画面");
            await Expect("比赛仪表", 35); await Task.Delay(4000);
            vm.PresentRaceMessage("【自动化切换测试】目前第四名，与前车相差两秒。此消息为验证内容。");
            await Expect("DeltaTR 画面");
            await Expect("比赛仪表", 35);
            Record("PASS manual open/return and two automatic message returns; physical result requires user observation");
        }
        finally
        {
            if (vm.WheelButtonText.Contains("关闭")) vm.ToggleWheelScreenCommand.Execute(null);
            await Task.Delay(2500);
        }
    }
}
