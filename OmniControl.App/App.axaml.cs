using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OmniControl.App.Services;
using OmniControl.App.Shell;

namespace OmniControl.App;

/// <summary>Start-up only: load settings and the accent, create the window, hand its lifetime to <see cref="WindowLifecycle"/>.</summary>
public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = AppSettings.Current;
        AccentService.Initialize(settings.Accent);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = new ShellViewModel();
            var window = new MainWindow { DataContext = shell };
            desktop.MainWindow = window;
            WindowLifecycle.Attach(this, desktop, window, shell, settings);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
