using System.IO;
using DeltaTR.App.ViewModels;

namespace DeltaTR.App.Diagnostics;

/// <summary>Explicit opt-in hardware check through real app query and screen commands.
/// Runs only inside --smoke-test with isolated settings and synthetic telemetry.</summary>
internal static class WheelSmoke
{
    public static async Task RunAsync(MainViewModel vm, string port, string folder)
    {
        var log = Path.Combine(folder, "wheel-smoke.log");
        void Record(string line) => File.AppendAllText(log, DateTimeOffset.Now.ToString("O") + " " + line + Environment.NewLine);
        vm.SelectedPage = vm.Navigation.Single(p => p.Key == "overlay");
        bool bridge = port == "bridge";
        vm.UsePitHouseBridge = bridge;
        if (!bridge) vm.SelectedWheelPort = vm.WheelPorts.Single(p => p.Port == port);
        vm.ToggleWheelScreenCommand.Execute(null);
        var until = DateTime.UtcNow.AddSeconds(48);
        bool Connected() => vm.WheelStatus.StartsWith(bridge ? "Pit House 已接收" : "KS Pro 已连接", StringComparison.Ordinal);
        while (!Connected())
        {
            if (DateTime.UtcNow >= until || vm.WheelStatus.Contains("失败") || vm.WheelStatus.Contains("请先退出"))
                throw new IOException("Wheel check could not connect: " + vm.WheelStatus);
            await Task.Delay(100);
        }
        Record(vm.WheelStatus + " · synthetic telemetry; physical rendering requires user observation");
        for (int i = 0; i < 6; i++)
        {
            string question = new[] { "当前轮胎温度多少", "还剩多少油", "排名" }[i % 3];
            await vm.SubmitQuestionAsync(question);
            Record(question + " => " + vm.Answer);
            await Task.Delay(14000);
            if (!Connected())
                throw new IOException("Wheel check disconnected: " + vm.WheelStatus);
        }
        Record("Completed app answer cycle; closing display on app shutdown.");
    }
}
