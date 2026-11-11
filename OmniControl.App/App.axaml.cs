using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OmniControl.App.ViewModels;
using OmniControl.App.Views;

namespace OmniControl.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = new ShellViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = shell,
            };
            desktop.Exit += (_, _) => shell.Volume.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}