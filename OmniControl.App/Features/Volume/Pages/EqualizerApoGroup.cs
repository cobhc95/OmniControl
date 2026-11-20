using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.App.Features.Volume.Pages.ApoDevices;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Pages;

/// The Wiring tab's Equalizer APO group: whose Equalizer APO this is (not installed, OmniControl's own copy, installed separately, or
/// uninstalled), with one link that does the next thing about it. OmniControl's copy is put on devices in its device window
/// (<see cref="ApoDevicesWindow"/>, "Choose devices" in the master's Method menu); a separate install's in its own Device Selector.
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
        Status = new FactItem("Status", () => _ = Guard.RunAsync("Equalizer APO: status link", StatusLinkAsync)) { ActionIsLink = true };
        Items.Add(Status);
        support.Changed += Sync;
        Sync();
    }

    private ApoState Apo => _support.Current?.Apo ?? ApoState.None;

    /// Opens OmniControl's Equalizer APO device window, or brings it to the front.
    public void OpenDevices() => _devices.Show(
        () => new ApoDevicesWindow { DataContext = new ApoDevicesViewModel(_support, _installer, _mixer, _runSetup) },
        closed => (closed.DataContext as IDisposable)?.Dispose(),
        "Couldn't open the Equalizer APO devices window");

    private void Sync()
    {
        if (_support.Current is not { } current || current.Apo.Equals(_shown)) return;
        var apo = _shown = current.Apo;
        var owner = apo.Ownership;
        (Status.Value, Status.Note, Status.ActionText) = owner.Owner switch
        {
            ApoOwner.Ours => ("Installed by OmniControl", "Choose devices in the master's Method menu puts it on devices.", "Remove OmniControl's Equalizer APO…"),
            ApoOwner.Separate => ("Installed separately", "Its own Device Selector chooses its devices; OmniControl only adds its effects.",
                apo.UpstreamDeviceSelector is null ? string.Empty : "Device Selector…"),
            _ when owner.SeparateRemoved => ("Uninstalled", "Equalizer APO was uninstalled; OmniControl can install its own copy.", InstallLink(apo)),
            _ => ("Not installed", apo.Bundled ? "Installed when a master effect first needs it." : "Its download page has the installer.", InstallLink(apo)),
        };
        Group.Summary = owner.Owner == ApoOwner.Ours ? $"{apo.OwnDevices.Count(d => d.Attached)} of {apo.OwnDevices.Count} on" : Status.Value;
    }

    private static string InstallLink(ApoState apo) => apo.Bundled ? "Install…" : "Download page…";

    /// The status card's link: install OmniControl's copy (or open the download page), open a separate install's Device Selector, or
    /// remove OmniControl's copy after asking. One at a time.
    private async Task StatusLinkAsync()
    {
        var apo = Apo;
        if (apo.Ownership.Owner == ApoOwner.Ours && !await _ask(new DialogMessage("Remove OmniControl's Equalizer APO?",
                "Every device it is on gets its own effects back, and Windows audio restarts for a moment.", "Remove", Danger: true)))
            return;
        if (apo.Ownership.Owner == ApoOwner.None && apo.Bundled && !await _ask(new DialogMessage("Install Equalizer APO?",
                "OmniControl installs its copy of Equalizer APO on no device yet; Windows audio then restarts (all sound stops for a few seconds). "
                + "Windows asks once for administrator rights.", "Install")))
            return;
        if (_busy) return;
        _busy = true;
        try
        {
            await _runSetup(null, apo.Ownership.Owner switch
            {
                ApoOwner.Ours => _installer.RemoveAsync(),
                ApoOwner.Separate => _installer.ChooseEqualizerApoDevicesAsync(),
                _ => apo.Bundled ? _installer.InstallAsync() : _installer.SetUpEqualizerApoAsync(),
            });
        }
        finally
        {
            _busy = false;
        }
    }

    public void Dispose()
    {
        _support.Changed -= Sync;
        _devices.Close();
    }
}
