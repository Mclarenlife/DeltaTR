namespace DeltaTR.Core;

public enum RacingGame { ACC, LMU, IRacing }

public static class GameNames
{
    public static string Display(this RacingGame game) => game switch
    {
        RacingGame.LMU => "Le Mans Ultimate",
        RacingGame.IRacing => "iRacing",
        _ => "ACC"
    };
}
