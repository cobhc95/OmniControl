using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Pages;

/// The Wiring tab's Equalizer APO group: whether Equalizer APO is installed (it is its own program, with its own installer, Device
/// Selector, uninstaller and Apps entry), and THE one step every place offers (<see cref="Devices"/>): "Equalizer APO devices…" opens its
/// Device Selector; while it is not installed, "Install Equalizer APO…" runs its official installer, which OmniControl ships.
public sealed class EqualizerApoGroup : IDisposable
{
    private readonly EffectsSupportService _support;
    private readonly EngineInstaller _installer;
    private readonly Func<string?, Task<SetupResult>, Task> _runSetup;
    private readonly CardAction _choose, _install;
    private ApoState? _shown;
    private bool _busy;

    public CardGroup Group { get; } = new("Volume/EqualizerApo", "Equalizer APO", string.Empty)
    {
        ExpandedByDefault = true,
        SummaryBesideName = true,
        Tip = "Runs the master's effects inside Windows on the devices it is on. It is its own program: its Device Selector chooses the devices, Windows' Installed apps removes it.",
    };

    /// The status card.
    public ObservableCollection<object> Items { get; } = new();

    /// The status card (also the page's search entry).
    public FactItem Status { get; }

    /// THE Equalizer APO step (the master's Method menu, its fix link, the FX device menu, Easy, this group's link): its Device Selector,
    /// or its installer while it is not installed.
    public CardAction Devices => Installed ? _choose : _install;

    public bool Installed => Apo.UpstreamInstalled;

    /// <param name="runSetup">Runs a setup step and says how it ended (the page's status line; a failure opens the message window).</param>
    public EqualizerApoGroup(EffectsSupportService support, EngineInstaller installer, Func<string?, Task<SetupResult>, Task> runSetup)
    {
        _support = support;
        _installer = installer;
        _runSetup = runSetup;
        var command = new AsyncRelayCommand(DevicesAsync);
        _choose = new CardAction(EffectsRules.ApoDevicesLabel, "Opens Equalizer APO's own Device Selector (administrator), where you tick the devices it runs on", command);
        _install = new CardAction(EffectsRules.ApoInstallLabel,
            "Runs Equalizer APO's official installer (one administrator prompt); its Device Selector then opens so you tick your speakers", command);
        Status = new FactItem("Status", () => _ = Guard.RunAsync("Equalizer APO: step", DevicesAsync)) { ActionIsLink = true };
        Items.Add(Status);
        support.Changed += Sync;
        Sync();
    }

    private ApoState Apo => _support.Current?.Apo ?? ApoState.None;

    /// Opens Equalizer APO's Device Selector, or runs its installer while it is not installed. One at a time.
    public async Task DevicesAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (Apo.UpstreamInstalled) await _runSetup(null, _installer.ChooseEqualizerApoDevicesAsync());
            else await _runSetup(EngineInstaller.Installing, _installer.InstallAsync());
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
        (Status.Value, Status.Note) = apo.UpstreamInstalled
            ? ("Installed separately", "Its own Device Selector chooses its devices; OmniControl only adds its effects.")
            : ("Not installed", "OmniControl brings its official installer.");
        Status.ActionText = Devices.Label;
        Group.Summary = Status.Value;
    }

    public void Dispose() => _support.Changed -= Sync;
}
