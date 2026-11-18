using Avalonia;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Mouse;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
#if DEBUG
        Showcase.ShowcaseOptions.UseThrowAwayData(args); // the screenshot export runs on empty data, before anything reads a setting
#endif
        // The elevated setup mode (OmniControl APO install/remove) runs and exits before anything of the app starts.
        if (EngineSetupCommand.TryRun(args) is { } exitCode) return exitCode;
        // A second start only asks the running copy to open its window (a start-up entry's start asks nothing), before it can touch any
        // file (a staged import, settings).
        var restart = AppRestart.IsRestart(args);
        var askToOpen = !StartupRegistration.IsBackgroundStart(args);
        if (!StartupMode.Inert && !restart && SingleInstance.HandOff(askToOpen)) return 0;
        ProcessPower.StayResponsive();
        AppRestart.BeforeStart(args);
        if (AdministratorMode.RelaunchIfWanted(args)) return 0;
        using var instance = StartupMode.Inert ? null : SingleInstance.Claim(restart ? Timing.RestartWait : TimeSpan.Zero, askToOpen);
        if (!StartupMode.Inert && instance is null) return 0;
        return BuildAvaloniaApp(instance).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>For the designer.</summary>
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(null);

    private static AppBuilder BuildAvaloniaApp(SingleInstance? instance) =>
        AppBuilder.Configure(() => new App(instance))
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
