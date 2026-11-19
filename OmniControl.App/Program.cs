using Avalonia.Logging;
using OmniControl.App.Features.Input.Mouse;

namespace OmniControl.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Setup's --quit only asks the running copy to quit, its --sign-in-task only adds or removes the task. A second start only asks the
        // running copy to open its window (a start-up entry's start asks nothing), before it can touch any file (a staged import, settings).
        if (SingleInstance.IsQuit(args)) return SingleInstance.AskToQuit();
        if (AdministratorMode.FromSetup(args) is { } done) return done;
        var restart = AppRestart.IsRestart(args);
        var askToOpen = !StartupRegistration.IsBackgroundStart(args);
        if (!restart && SingleInstance.HandOff(askToOpen)) return 0;
        ProcessPower.StayResponsive();
        AppRestart.BeforeStart(args);
        if (AdministratorMode.RelaunchIfWanted(args, () => MouseSettings.Load().RunAsAdministrator)) return 0;
        using var instance = SingleInstance.Claim(restart ? Timing.RestartWait : TimeSpan.Zero, askToOpen);
        if (instance is null) return 0;
        var exitCode = BuildAvaloniaApp(instance).StartWithClassicDesktopLifetime(args);
        AppDataStore.FlushSaves();
        AppDataStore.FlushLogs(); // the shut-down's last lines (the plug-ins' teardown)
        return exitCode;
    }

    /// For the designer.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(null);

    /// Windows only, drawn by Skia in Avalonia's default Win32 rendering order (ANGLE over Direct3D first, then software). Text uses the
    /// system font (Segoe UI), so no font is bundled. Avalonia's own trace log keeps errors only.
    private static AppBuilder BuildAvaloniaApp(SingleInstance? instance) =>
        AppBuilder.Configure(() => new App(instance))
            .UseWin32()
            .UseSkia()
            .LogToTrace(LogEventLevel.Error);
}
