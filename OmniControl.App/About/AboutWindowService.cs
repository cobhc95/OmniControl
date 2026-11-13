namespace OmniControl.App.About;

/// <summary>Opens the About and credits window, one at a time (a second request brings it to the front).</summary>
public sealed class AboutWindowService
{
    private readonly string _version;
    private readonly Action<string> _report;
    private AboutWindow? _open;

    public AboutWindowService(string version, Action<string> report)
    {
        _version = version;
        _report = report;
    }

    public void Open()
    {
        if (_open is not null) { _open.Activate(); return; }
        _open = new AboutWindow { DataContext = new AboutViewModel(_version, _report) };
        _open.Closed += (_, _) => _open = null;
        Services.OwnedWindow.Show(_open);
    }

    public void Close() => _open?.Close();
}
