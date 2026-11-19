namespace OmniControl.App.Features.OtherPcs.SettingsImport;

/// Opens "Import settings from a PC" for a paired PC that is online, from the Other PCs tab's menu and from App settings > Backup. One
/// window at a time (asking again brings it to the front). The paired PCs and the way to them come from <see cref="ISettingsTransport"/>.
public sealed class ImportFromPcService
{
    private readonly ISettingsTransport _transport;
    private readonly ImportHooks _hooks;
    private readonly SingleWindow<ImportFromPcWindow> _window;

    public ImportFromPcService(ISettingsTransport transport, ImportHooks hooks, Action<string> report)
    {
        _transport = transport;
        _hooks = hooks;
        _window = new SingleWindow<ImportFromPcWindow>(report);
    }

    /// The paired PCs that are connected now (the only ones that can be imported from).
    public IReadOnlyList<(string Id, string Name)> OnlinePcs() => _transport.OnlinePcs();

    public bool IsOnline(string pcId) => _transport.IsOnline(pcId);

    public void Open(string pcId) => _window.Show(
        () => new ImportFromPcWindow { DataContext = new ImportFromPcViewModel(pcId, _transport, _hooks) },
        closed => (closed.DataContext as IDisposable)?.Dispose(),
        "Couldn't open the import window");
}
