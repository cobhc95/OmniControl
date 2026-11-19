using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OmniControl.App.Shell;

namespace OmniControl.App;

/// Start-up only: load settings, the background colour and the accent, create the window, hand its lifetime to <see cref="WindowLifecycle"/>.
public partial class App : Application
{
    private readonly SingleInstance? _instance;
    private UiWatchdog? _watchdog; // logs once when the UI thread stops answering

    public App() { }

    /// <param name="instance">This run's claim to be the one copy; a later start asks it to open the window.</param>
    internal App(SingleInstance? instance) => _instance = instance;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        CrashGuard.Install();
        _watchdog = new UiWatchdog();
        var settings = AppSettings.Current;
        SurfaceService.Initialize(settings.Background); // first: the accent's washes are blended into its page colour
        AccentService.Initialize(settings.Accent);
        ValueWheel.Register();
        NumberText.Register();
        SearchClear.Register();
        EmptyToolTips.Register();
        ComboKeys.Register();
        DialogKeys.Register();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Exit += (_, _) => _watchdog?.Dispose();
            var shell = new ShellViewModel();
            DataTemplates.AddRange(shell.Templates);
            var window = new MainWindow { DataContext = shell };
            desktop.MainWindow = window;
            WindowLifecycle.Attach(this, desktop, window, shell, settings, StartupRegistration.IsBackgroundStart(desktop.Args ?? Array.Empty<string>()));
            StartupRegistration.RepairIfMoved();
            _instance?.Listen(() => Dispatcher.UIThread.Post(() => AppLifecycle.RequestShow("another start of OmniControl (it ended and handed over)")),
                () => Dispatcher.UIThread.Post(AppLifecycle.RequestQuit));
        }
        // Once start-up has built the pages and drawn the pop-up (its warm-up spans several frames, so "idle" can come too early), give
        // what start-up needed only back to Windows.
        DispatcherTimer.RunOnce(() => Guard.Run("Give memory back", ProcessPower.Compact), Timing.CompactAfterStart);
        base.OnFrameworkInitializationCompleted();
    }
}
