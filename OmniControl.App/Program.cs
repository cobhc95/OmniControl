using System.Runtime.Loader;
using Avalonia.Logging;
using OmniControl.App.Features.Input.Mouse;

namespace OmniControl.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Before anything loads an optional assembly: deps.json does not list them (OptionalFiles.Resolve). --check-start is for the build's own check: it starts nothing.
        AssemblyLoadContext.Default.Resolving += OptionalFiles.Resolve;
        if (args.Contains("--check-start")) return OptionalFiles.LoadPresent() ? 0 : 2;
        // The normal-rights copy that opens one thing for an elevated run, the network service (started by Windows) and its install or removal run nothing else. Setup's
        // --sign-in-task only adds, checks or removes the task. A second start only asks the running copy to open its window (a start-up
        // entry's start asks nothing), before it can touch any file (a staged import, settings). A copy the sign-in task started reads only
        // the arguments a start of its own would give it.
        args = AdministratorMode.Trusted(args);
        if (NormalRights.FromArguments(args) is { } opened) return opened;
        if (NetworkServiceMode.FromArguments(args) is { } served) return served;
        if (AdministratorMode.FromSetup(args) is { } done) return done;
        var restart = AppRestart.IsRestart(args);
        var askToOpen = !StartupRegistration.IsBackgroundStart(args);
        if (!restart && SingleInstance.HandOff(askToOpen)) return 0;
        ProcessPower.StayResponsive();
        AppRestart.BeforeStart(args);
        if (AdministratorMode.RelaunchIfWanted(args, () => MouseSettings.Load().RunAsAdministrator)) return 0;
        using var instance = SingleInstance.Claim(restart ? Timing.RestartWait : TimeSpan.Zero, askToOpen);
        if (instance is null) return 0;
        ProcessPower.LimitDrawingCaches();
        var exitCode = BuildAvaloniaApp(instance).StartWithClassicDesktopLifetime(args);
        AppDataStore.FlushSaves();
        AppDataStore.FlushLogs(); // the shut-down's last lines (the plug-ins' teardown)
        return exitCode;
    }

    /// For the designer.
    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(null);

    /// Windows only, drawn by Skia in software unless App settings' "Use GPU drawing" is on: then Avalonia's own default order, ANGLE (Direct3D)
    /// with software as fallback. Software never loads ANGLE, Direct3D or the GPU driver (about 50 to 100 MB of private memory and as much GPU
    /// memory for windows this small; transparent windows use Windows' blur-behind with an empty region). Text uses the system font (Segoe UI),
    /// so no font is bundled. Avalonia's own trace log keeps errors only.
    private static AppBuilder BuildAvaloniaApp(SingleInstance? instance) =>
        AppBuilder.Configure(() => new App(instance))
            .UseWin32()
            .UseSkia()
            .With(new Win32PlatformOptions
            {
                RenderingMode = instance is not null && AppSettings.Current.GpuDrawing
                    ? new[] { Win32RenderingMode.AngleEgl, Win32RenderingMode.Software }
                    : new[] { Win32RenderingMode.Software },
            })
            .LogToTrace(LogEventLevel.Error);
}
