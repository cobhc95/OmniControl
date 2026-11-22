using System.Text.Json;
using OmniControl.App.Shared.Services.Backup;
using OmniControl.App.Shared.Ui.SettingsImport;

namespace OmniControl.App.Features.OtherPcs.SettingsImport;

/// Opens "Import settings from a PC" (the shared <see cref="SettingsWindow"/>) for a paired PC that is online, from the Other PCs tab's menu
/// and from App settings > Backup. One window at a time (asking again brings it to the front). The paired PCs and the way to them come from
/// <see cref="ISettingsTransport"/>.
public sealed class ImportFromPcService
{
    private readonly ISettingsTransport _transport;
    private readonly ImportHooks _hooks;
    private readonly SingleWindow<SettingsWindow> _window;

    public ImportFromPcService(ISettingsTransport transport, ImportHooks hooks, Action<string> report)
    {
        _transport = transport;
        _hooks = hooks;
        _window = new SingleWindow<SettingsWindow>(report);
    }

    /// The paired PCs that are connected now (the only ones that can be imported from).
    public IReadOnlyList<(string Id, string Name)> OnlinePcs() => _transport.OnlinePcs();

    public bool IsOnline(string pcId) => _transport.IsOnline(pcId);

    public void Open(string pcId) => _window.Show(
        () => new SettingsWindow { DataContext = new SettingsWindowViewModel(new PcSource(_transport, pcId), _hooks) },
        closed => (closed.DataContext as IDisposable)?.Dispose(),
        "Couldn't open the import window");

    /// A paired PC as the window's source: its areas and files arrive as json over the transport.
    private sealed class PcSource(ISettingsTransport transport, string pcId) : IImportSource
    {
        public string Name { get; } = transport.NameOf(pcId);

        public IReadOnlyList<ImportArea> Areas => ImportAreas.Shared;

        public async Task<(IReadOnlyList<AreaOffer>? Offers, string? Why)> OffersAsync()
        {
            var (offers, why) = Parse<List<AreaOffer>>(await transport.OffersAsync(pcId), $"{Name} sent something OmniControl could not read.");
            return (offers, why);
        }

        public async Task<(IReadOnlyDictionary<string, string>? Files, string? Why)> AreaAsync(string area)
        {
            var (files, why) = Parse<Dictionary<string, string>>(await transport.AreaAsync(pcId, area), "What arrived could not be read.");
            return (files, why);
        }

        private static (T? Value, string? Why) Parse<T>(ImportReply reply, string unreadable) where T : class
        {
            if (reply.Payload is null) return (null, reply.Why);
            try
            {
                return JsonSerializer.Deserialize<T>(reply.Payload, PcProtocol.Json) is { } value ? (value, null) : (null, unreadable);
            }
            catch (JsonException)
            {
                return (null, unreadable);
            }
        }
    }
}
