using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.App.Features.Volume.Pages.ApoDevices;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Pages;

/// The Wiring tab's Equalizer APO group: whose Equalizer APO this is (not installed, OmniControl's own copy, installed separately, or
/// uninstalled), and THE "Equalizer APO devices…" step every place offers (<see cref="Devices"/>): OmniControl's copy is installed and
/// uninstalled per device only in its device window (<see cref="ApoDevicesWindow"/>); a separate install's devices in its own Device
/// Selector; a build without the bundled copy opens the download page.
public sealed class EqualizerApoGroup : IDisposable
{
    private readonly EffectsSupportService _support;
    private readonly EngineInstaller _installer;
    private readonly MixerService _mixer;
    private readonly Func<string?, Task<SetupResult>, Task> _runSetup;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly SingleWindow<ApoDevicesWindow> _devices;
    private ApoState? _shown;
    private bool _busy;

    public CardGroup Group { get; } = new("Volume/EqualizerApo", "Equalizer APO", string.Empty)
    {
        ExpandedByDefault = true,
        SummaryBesideName = true,
        Tip = "Runs the master's effects inside Windows on the devices it is on. OmniControl brings its own copy unless one is installed separately.",
    };

    /// The status card.
    public ObservableCollection<object> Items { get; } = new();

    /// The status card (also the page's search entry).
    public FactItem Status { get; }

    /// THE "Equalizer APO devices…" step (the master's Method menu, its fix link, the FX device menu, Easy, this group's link).
    public CardAction Devices { get; }

    /// <param name="runSetup">Runs a setup step and says how it ended (the page's status line; a failure opens the message window).</param>
    public EqualizerApoGroup(EffectsSupportService support, EngineInstaller installer, MixerService mixer, Func<string?, Task<SetupResult>, Task> runSetup,
        Func<DialogMessage, Task<bool>> ask, Action<string> report)
    {
        _support = support;
        _installer = installer;
        _mixer = mixer;
        _runSetup = runSetup;
        _ask = ask;
        _devices = new SingleWindow<ApoDevicesWindow>(report);
        Devices = new CardAction(EffectsRules.ApoDevicesLabel, "Installs or uninstalls Equalizer APO per device (a separate install: its own Device Selector)",
            new AsyncRelayCommand(DevicesAsync));
        Status = new FactItem("Status", () => _ = Guard.RunAsync("Equalizer APO: devices link", DevicesAsync)) { ActionIsLink = true, ActionText = EffectsRules.ApoDevicesLabel };
        Items.Add(Status);
        support.Changed += Sync;
        Sync();
    }

    private ApoState Apo => _support.Current?.Apo ?? ApoState.None;

    /// Opens OmniControl's device window (or brings it to the front), a separate install's Device Selector, or the download page. One at a time.
    public async Task DevicesAsync()
    {
        var apo = Apo;
        if (apo.Ownership.Owner != ApoOwner.Separate && (apo.Ownership.Owner == ApoOwner.Ours || apo.Bundled))
        {
            _devices.Show(() => new ApoDevicesWindow { DataContext = new ApoDevicesViewModel(_support, _installer, _mixer, _runSetup, _ask) },
                closed => (closed.DataContext as IDisposable)?.Dispose(), "Couldn't open the Equalizer APO devices window");
            return;
        }
        if (_busy) return;
        _busy = true;
        try
        {
            await _runSetup(null, apo.Ownership.Owner == ApoOwner.Separate ? _installer.ChooseEqualizerApoDevicesAsync() : _installer.OpenDownloadPageAsync());
        }
        finally
        {
            _busy = false;
        }
    }

    private void Sync()
    {
        if (_support.Current is not { } current || current.Apo.Equals(_shown)) return;
        var apo = _shown = current.Apo;
        var owner = apo.Ownership;
        (Status.Value, Status.Note) = owner.Owner switch
        {
            ApoOwner.Ours => ("Installed by OmniControl", "Its devices window installs or uninstalls it per device."),
            ApoOwner.Separate => ("Installed separately", "Its own Device Selector chooses its devices; OmniControl only adds its effects."),
            _ when owner.SeparateRemoved => ("Uninstalled", "Equalizer APO was uninstalled; OmniControl can put its own copy on a device."),
            _ => ("Not installed", apo.Bundled ? "Its devices window puts it on a device." : "Its download page has the installer."),
        };
        Group.Summary = owner.Owner == ApoOwner.Ours ? $"{apo.OwnDevices.Count(d => d.Attached)} of {apo.OwnDevices.Count} on" : Status.Value;
    }

    public void Dispose()
    {
        _support.Changed -= Sync;
        _devices.Close();
    }
}
