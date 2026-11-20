using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Pages;

/// The Wiring tab's Equalizer APO group: whose Equalizer APO this is (not installed, OmniControl's own copy, installed separately, or
/// uninstalled), with one link that does the next thing about it; while OmniControl's copy is installed, one switch per playback and
/// recording device that puts it on or takes it off (each change restarts Windows audio once and is checked, <see cref="EngineInstaller"/>),
/// its honest status and an "Advanced…" menu with Device Selector's install mode and troubleshooting options. A separate install's
/// devices are chosen in its own Device Selector, so no switches show then.
public sealed class EqualizerApoGroup : IDisposable
{
    private readonly EffectsSupportService _support;
    private readonly EngineInstaller _installer;
    private readonly Func<string?, Task<SetupResult>, Task> _runSetup;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private ApoState? _shown;
    private bool _busy;

    public CardGroup Group { get; } = new("Volume/EqualizerApo", "Equalizer APO", string.Empty)
    {
        ExpandedByDefault = true,
        SummaryBesideName = true,
        Tip = "Runs the master's effects inside Windows on the devices it is on. OmniControl brings its own copy unless one is installed separately.",
    };

    /// The status card, then one switch card per device.
    public ObservableCollection<object> Items { get; } = new();

    /// The status card (also the page's search entry).
    public FactItem Status { get; }

    /// <param name="runSetup">Runs a setup step and says how it ended (the page's status line; a failure opens the message window).</param>
    public EqualizerApoGroup(EffectsSupportService support, EngineInstaller installer, Func<string?, Task<SetupResult>, Task> runSetup, Func<DialogMessage, Task<bool>> ask)
    {
        _support = support;
        _installer = installer;
        _runSetup = runSetup;
        _ask = ask;
        Status = new FactItem("Status", () => _ = Guard.RunAsync("Equalizer APO: status link", StatusLinkAsync)) { ActionIsLink = true };
        Items.Add(Status);
        support.Changed += Sync;
        Sync();
    }

    private ApoState Apo => _support.Current?.Apo ?? ApoState.None;

    private void Sync()
    {
        if (_support.Current is not { } current || current.Apo.Equals(_shown)) return;
        var apo = _shown = current.Apo;
        var owner = apo.Ownership;
        (Status.Value, Status.Note, Status.ActionText) = owner.Owner switch
        {
            ApoOwner.Ours => ("Installed by OmniControl", "Attaching a device restarts Windows audio for a moment.", "Remove OmniControl's Equalizer APO…"),
            ApoOwner.Separate => ("Installed separately", "Its own Device Selector chooses its devices; OmniControl only adds its effects.",
                apo.UpstreamDeviceSelector is null ? string.Empty : "Device Selector…"),
            _ when owner.SeparateRemoved => ("Uninstalled", "Equalizer APO was uninstalled; OmniControl can install its own copy.", InstallLink(apo)),
            _ => ("Not installed", apo.Bundled ? "Installed when a master effect first needs it." : "Its download page has the installer.", InstallLink(apo)),
        };
        while (Items.Count > 1) Items.RemoveAt(Items.Count - 1);
        foreach (var device in apo.OwnDevices.OrderBy(d => d.Input)) Items.Add(Card(device));
        Group.Summary = owner.Owner == ApoOwner.Ours ? $"{apo.OwnDevices.Count(d => d.Attached)} of {apo.OwnDevices.Count} on" : Status.Value;
    }

    private static string InstallLink(ApoState apo) => apo.Bundled ? "Install…" : "Download page…";

    private ToggleItem Card(ApoDeviceEntry device) => new(device.Name, device.Attached, on => _ = Guard.RunAsync("Equalizer APO: device switch", () => SwitchAsync(device, on)))
    {
        Note = device.Input ? "Microphone" : null,
        Tags = TagsOf(device),
        MenuText = device.Attached ? "Advanced…" : null,
        Menu = device.Attached ? () => AdvancedRows(device) : null,
    };

    /// The device's status: the check of OmniControl's last change of it.
    private static IReadOnlyList<TagItem> TagsOf(ApoDeviceEntry device) => (device.Attached, device.Check) switch
    {
        (true, ApoSetup.Running) => new[] { new TagItem("Attached · running", TagTone.Good, "Equalizer APO started on it after the last change.") },
        (true, ApoSetup.NeedsRestart) => new[] { new TagItem("Needs an audio restart", TagTone.Caution, "Windows audio didn't restart, so it starts after the next restart of Windows.") },
        (false, ApoSetup.Blocked) => new[] { new TagItem("This device's driver blocks it", TagTone.Risk, "Windows didn't start Equalizer APO on it, so its own effects were put back. Advanced… picks another install mode once it is on.") },
        _ => Array.Empty<TagItem>(),
    };

    private async Task SwitchAsync(ApoDeviceEntry device, bool on)
    {
        if (!Begin()) return;
        try
        {
            await _runSetup("Windows audio restarts for a moment…", on ? _installer.AttachAsync(device, device.State) : _installer.DetachAsync(device));
        }
        finally
        {
            End();
        }
    }

    /// Device Selector's troubleshooting options for one device: the install mode, then which APOs go on and whether the driver's own
    /// ones keep running inside Equalizer APO. Each pick puts it on again with that option (one prompt, one audio restart).
    private IReadOnlyList<MenuRowItem> AdvancedRows(ApoDeviceEntry device)
    {
        var s = device.State;
        var rows = new List<MenuRowItem> { MenuRowItem.Header("Install mode") };
        rows.AddRange(Enum.GetValues<ApoInstallMode>().Select(mode => Row(ModeName(mode), s.Mode == mode, device, s with
        {
            Mode = mode,
            OriginalPreMix = s.OriginalPreMix && device.HasOriginalPreMix(mode),
            OriginalPostMix = s.OriginalPostMix && device.HasOriginalPostMix(mode),
        })));
        rows.Add(MenuRowItem.Header("Troubleshooting", ruleAbove: true));
        rows.Add(Switch("Pre-mix APO", s.PreMix, device, s with { PreMix = !s.PreMix }));
        if (!device.Input) rows.Add(Switch("Post-mix APO", s.PostMix, device, s with { PostMix = !s.PostMix }));
        if (s.PreMix && device.HasOriginalPreMix(s.Mode)) rows.Add(Switch("Keep the driver's pre-mix effects", s.OriginalPreMix, device, s with { OriginalPreMix = !s.OriginalPreMix }));
        if (s.PostMix && !device.Input && device.HasOriginalPostMix(s.Mode))
            rows.Add(Switch("Keep the driver's post-mix effects", s.OriginalPostMix, device, s with { OriginalPostMix = !s.OriginalPostMix }));
        rows.Add(Switch("Allow silent buffer modification", s.AllowSilentBuffer, device, s with { AllowSilentBuffer = !s.AllowSilentBuffer }));
        return rows;
    }

    private static string ModeName(ApoInstallMode mode) => mode switch
    {
        ApoInstallMode.LfxGfx => "Install as LFX/GFX",
        ApoInstallMode.SfxMfx => "Install as SFX/MFX",
        _ => "Install as SFX/EFX",
    };

    private MenuRowItem Row(string text, bool on, ApoDeviceEntry device, ApoInstallState state) =>
        new(text, string.Empty, on, "Windows audio restarts for a moment", Pick(device, state));

    private MenuRowItem Switch(string text, bool on, ApoDeviceEntry device, ApoInstallState state) =>
        new(text, string.Empty, on, "Windows audio restarts for a moment", Pick(device, state), IsSwitch: true);

    private IAsyncRelayCommand Pick(ApoDeviceEntry device, ApoInstallState state) => new AsyncRelayCommand(() => Guard.RunAsync("Equalizer APO: device option", async () =>
    {
        if (state == device.State || !Begin()) return;
        try
        {
            await _runSetup("Windows audio restarts for a moment…", state.PreMix || state.PostMix
                ? _installer.AttachAsync(device, state)
                : Task.FromResult(new SetupResult("Keep the pre-mix or the post-mix APO on, or switch the device off.")));
        }
        finally
        {
            End();
        }
    }));

    /// The status card's link: install OmniControl's copy (or open the download page), open a separate install's Device Selector, or
    /// remove OmniControl's copy after asking.
    private async Task StatusLinkAsync()
    {
        var apo = Apo;
        if (apo.Ownership.Owner == ApoOwner.Ours && !await _ask(new DialogMessage("Remove OmniControl's Equalizer APO?",
                "Every device it is on gets its own effects back, and Windows audio restarts for a moment.", "Remove", Danger: true)))
            return;
        if (!Begin()) return;
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
            End();
        }
    }

    /// One step at a time from this group: a second change while one runs is not started (its switch shows the device's state again).
    private bool Begin()
    {
        if (!_busy) return _busy = true;
        Reshow();
        return false;
    }

    /// The step ended: the cards show what is in place again (a refused prompt leaves the switch where it was; a done step is shown again
    /// once the fresh look arrives).
    private void End()
    {
        _busy = false;
        Reshow();
    }

    private void Reshow()
    {
        _shown = null;
        Sync();
    }

    public void Dispose() => _support.Changed -= Sync;
}
