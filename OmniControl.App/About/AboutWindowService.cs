using OmniControl.App.Services;

namespace OmniControl.App.About;

/// <summary>Opens the About and credits window, one at a time (a second request brings it to the front).</summary>
public sealed class AboutWindowService
{
    private readonly Action<string> _report;
    private readonly SingleWindow<AboutWindow> _window;

    public AboutWindowService(Action<string> report)
    {
        _report = report;
        _window = new SingleWindow<AboutWindow>(report);
    }

    public void Open() => _window.Show(() => new AboutWindow { DataContext = new AboutViewModel(_report) }, _ => { }, "Couldn't open About");

    public void Close() => _window.Close();
}
