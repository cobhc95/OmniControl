using OmniControl.App.Pages;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using OmniControl.Core.Engine;
using OmniControl.App.Services;
using OmniControl.App.Shell;

namespace OmniControl.App;

/// <summary>Start-up only: load settings and the accent, create the window, hand its lifetime to <see cref="WindowLifecycle"/>.</summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        CrashGuard.Install();
        var settings = AppSettings.Current;
        AccentService.Initialize(settings.Accent);
        ValueWheel.Register();
        NumberText.Register();
        SearchClear.Register();
        EmptyToolTips.Register();
        ComboKeys.Register();
        DialogKeys.Register();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = new ShellViewModel();
            var window = new MainWindow { DataContext = shell };
            desktop.MainWindow = window;
#if DEBUG
            Showcase.ShowcaseRun.AttachChord(window);
            if (!Showcase.ShowcaseRun.TryStart(desktop, window, shell))
#endif
            {
                WindowLifecycle.Attach(this, desktop, window, shell, settings, StartupRegistration.IsBackgroundStart(desktop.Args ?? Array.Empty<string>()));
                StartupRegistration.RepairIfMoved();
            }
        }
        // Once start-up has built the pages and drawn the pop-up (its warm-up spans several frames, so "idle" can come too early),
        // keep that memory resident so an idle spell can't page it out.
        DispatcherTimer.RunOnce(ProcessPower.KeepResident, Timing.KeepResidentAfterStart);
        base.OnFrameworkInitializationCompleted();
    }
}
