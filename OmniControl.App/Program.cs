using Avalonia;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The elevated setup mode (OmniControl APO install/remove) runs and exits before anything of the app starts.
        if (EngineSetupCommand.TryRun(args) is { } exitCode) return exitCode;
        ProcessPower.StayResponsive();
        AppRestart.BeforeStart(args);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
