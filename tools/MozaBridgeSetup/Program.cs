using DeltaTR.Hardware.Moza;

try
{
    switch (args.FirstOrDefault())
    {
        case "install" when args.Length == 2:
            PitHouseBridgeInstaller.Install(args[1], BridgeSettings.LoadOrCreate());
            Console.WriteLine("Bridge installed with verified backup. Restart Pit House to load it.");
            break;
        case "restore":
            PitHouseBridgeInstaller.Restore();
            Console.WriteLine("Original file restored. Restart Pit House to unload bridge.");
            break;
        case "status": Console.WriteLine(PitHouseBridgeInstaller.IsInstalled ? "Installed; file hash verified" : "Not installed or file changed"); break;
        default: Console.Error.WriteLine("Usage: MozaBridgeSetup install <bin/MOZA Pit House.exe> | restore | status"); return 2;
    }
    return 0;
}
catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
