using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Pages.ApoDevices;

/// OmniControl's own Equalizer APO device window (Equalizer APO 1.4.2's Device Selector for the bundled copy): every playback and
/// recording device with its tick and status; a tick attaches, an untick detaches, live (one prompt and one audio restart each, then
/// checked, <see cref="EngineInstaller"/>). "Troubleshooting options" shows Device Selector's troubleshooting panel for the selected
/// attached device (DeviceSelector.ui/.cpp: Install APO and Use original APO for pre-mix and post-mix, Auto, the install mode, Allow
/// silent buffer modification), each change putting it on again with that option as Device Selector's reinstall does. Reads the
/// effects support on open and whenever it changes (devices come or go), never by a timer.
public sealed partial class ApoDevicesViewModel : ObservableObject, IDisposable
{
    private const string Restarting = "Windows audio restarts for a moment…";

    private readonly EffectsSupportService _support;
    private readonly EngineInstaller _installer;
    private readonly MixerService _mixer;
    private readonly Func<string?, Task<SetupResult>, Task> _runSetup;
    private readonly List<ApoDeviceRow> _rows = new();
    private bool _busy;

    public ApoDevicesViewModel(EffectsSupportService support, EngineInstaller installer, MixerService mixer, Func<string?, Task<SetupResult>, Task> runSetup)
    {
        (_support, _installer, _mixer, _runSetup) = (support, installer, mixer, runSetup);
        support.Changed += Sync;
        support.Refresh();
        Sync();
    }

    /// Headings and rows in order.
    public ObservableCollection<object> Items { get; } = new();

    [ObservableProperty] private string _managedTag = "Managed by OmniControl";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsOptions), nameof(ShowsPick))] private bool _troubleshooting;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(ShowsOptions), nameof(ShowsPick))] private ApoDeviceRow? _selected;

    private ApoDeviceEntry? Chosen => Selected?.Entry is { Attached: true } e ? e : null;
    private ApoInstallState? State => Chosen?.State;

    /// Device Selector's page 1 (the options) when exactly one attached device is selected, else page 0 (its hint).
    public bool ShowsOptions => Troubleshooting && Chosen is not null;
    public bool ShowsPick => Troubleshooting && Chosen is null;

    public bool CanPostMix => Chosen is { Input: false };
    public bool CanOriginalPreMix => Chosen is { } e && e.HasOriginalPreMix(e.State.Mode) && e.State.PreMix;
    public bool CanOriginalPostMix => Chosen is { Input: false } e && e.HasOriginalPostMix(e.State.Mode) && e.State.PostMix;

    public bool InstallPreMix { get => State?.PreMix == true; set => Change(s => s with { PreMix = value }); }
    public bool InstallPostMix { get => State?.PostMix == true; set => Change(s => s with { PostMix = value }); }
    public bool UseOriginalPreMix { get => Chosen is { } e && e.State.OriginalPreMix && e.HasOriginalPreMix(e.State.Mode); set => Change(s => s with { OriginalPreMix = value }); }
    public bool UseOriginalPostMix { get => Chosen is { } e && e.State.OriginalPostMix && e.HasOriginalPostMix(e.State.Mode); set => Change(s => s with { OriginalPostMix = value }); }
    public bool AllowSilentBuffer { get => State?.AllowSilentBuffer == true; set => Change(s => s with { AllowSilentBuffer = value }); }
    public bool AutoAdjust { get => State?.AutoAdjust == true; set => Change(s => s with { AutoAdjust = value }); }

    public string ModeText => ModeName(State?.Mode ?? ApoInstallMode.LfxGfx);

    /// The install mode drop-down's rows, in Device Selector's order.
    public IReadOnlyList<MenuRowItem> ModeRows() => Enum.GetValues<ApoInstallMode>()
        .Select(mode => new MenuRowItem(ModeName(mode), string.Empty, State?.Mode == mode, Restarting, new RelayCommand(() => Change(s => s with { Mode = mode }))))
        .ToList();

    private static string ModeName(ApoInstallMode mode) => mode switch
    {
        ApoInstallMode.LfxGfx => "Install as LFX/GFX",
        ApoInstallMode.SfxMfx => "Install as SFX/MFX",
        _ => "Install as SFX/EFX",
    };

    /// Up/Down: the next or previous device, wrapping at both ends.
    public void Move(int direction)
    {
        if (_rows.Count == 0) return;
        var at = Selected is null ? (direction > 0 ? -1 : 0) : _rows.IndexOf(Selected);
        Pick(_rows[Wrap.Index(at + direction, _rows.Count)]);
    }

    /// Space: ticks or unticks the selected device.
    public void ToggleSelected()
    {
        if (Selected is { CanAttach: true } row) row.IsChecked = !row.IsChecked;
    }

    private void Pick(ApoDeviceRow row)
    {
        if (Selected is { } old) old.IsSelected = false;
        row.IsSelected = true;
        Selected = row;
        OptionsChanged();
    }

    /// While a step runs the rows keep showing it; its end shows what is in place.
    private void Sync()
    {
        if (!_busy) Show();
    }

    private void Show()
    {
        if (_support.Current?.Apo is not { } apo) return;
        ManagedTag = apo.UpstreamVersion is { } v ? $"Managed by OmniControl · {v.ToString(3)}" : "Managed by OmniControl";
        var defaults = _mixer.Devices.Concat(_mixer.RecordingDevices).Where(d => d.IsDefault).Select(d => ApoIds.DeviceKeyOf(d.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wanted = Group(apo, defaults, input: false).Concat(Group(apo, defaults, input: true)).ToList();
        if (!wanted.Select(w => w.Key).SequenceEqual(_rows.Select(r => r.Key), StringComparer.OrdinalIgnoreCase))
        {
            var selected = Selected?.Key;
            _rows.Clear();
            Items.Clear();
            Selected = null;
            foreach (var input in new[] { false, true })
            {
                var group = wanted.Where(w => w.Input == input).ToList();
                if (group.Count == 0) continue;
                Items.Add(new ApoDeviceHeading(input ? "Recording devices" : "Playback devices"));
                foreach (var w in group)
                {
                    var row = new ApoDeviceRow(w.Key, OnToggled, Pick);
                    _rows.Add(row);
                    Items.Add(row);
                    if (string.Equals(w.Key, selected, StringComparison.OrdinalIgnoreCase)) Pick(row);
                }
            }
        }
        for (var i = 0; i < wanted.Count; i++) wanted[i].Show(_rows[i]);
        OptionsChanged();
    }

    private sealed record Wanted(string Key, bool Input, Action<ApoDeviceRow> Show);

    /// One group's rows: the devices OmniControl's copy can be put on, then the remote ones Windows gives no effect slots.
    private IEnumerable<Wanted> Group(ApoState apo, HashSet<string> defaults, bool input)
    {
        foreach (var e in apo.OwnDevices.Where(d => d.Input == input))
        {
            var (status, tone) = StatusOf(e);
            var suffix = (defaults.Contains(e.Guid) ? " · Default" : string.Empty) + (e.Unplugged ? " · Unplugged" : string.Empty) + (e.Disabled ? " · Disabled" : string.Empty);
            yield return new(e.Guid, input, row => row.Show(e, e.Connector, e.Device, status + suffix, tone));
        }
        var names = (input ? _mixer.RecordingDevices : _mixer.Devices).ToDictionary(d => d.Id, d => d.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var remote in (input ? apo.Microphones : apo.Playback).Where(p => !p.Attachable))
            yield return new(remote.EndpointId, input, row => row.Show(null, string.Empty, names.GetValueOrDefault(remote.EndpointId, remote.EndpointId),
                "Can't be attached: remote device", ApoRowTone.Plain));
    }

    private static (string Status, ApoRowTone Tone) StatusOf(ApoDeviceEntry e) => (e.Attached, e.Check) switch
    {
        (true, ApoSetup.Running) => ("Attached · running", ApoRowTone.Good),
        (true, ApoSetup.NeedsRestart) => ("Attached · needs an audio restart", ApoRowTone.Caution),
        (true, _) => ("Attached", ApoRowTone.Plain),
        (false, ApoSetup.Blocked) => ("This device's driver blocks it", ApoRowTone.Bad),
        _ => ("Can be attached", ApoRowTone.Plain),
    };

    private void OnToggled(ApoDeviceRow row, bool on)
    {
        Pick(row);
        if (row.Entry is { } e) Run("Equalizer APO: device tick", () => on ? _installer.AttachAsync(e, e.State) : _installer.DetachAsync(e));
    }

    /// A troubleshooting option of the selected device: put on again with it (Device Selector's reinstall).
    private void Change(Func<ApoInstallState, ApoInstallState> edit)
    {
        if (Chosen is not { } e) return;
        var state = edit(e.State);
        if (state == e.State) return;
        if (state.PreMix || state.PostMix) Run("Equalizer APO: device option", () => _installer.AttachAsync(e, state));
        else
        {
            Show();
            _ = Guard.RunAsync("Equalizer APO: device option", () => _runSetup(null, Task.FromResult(new SetupResult("Keep the pre-mix or the post-mix APO installed, or untick the device."))));
        }
    }

    /// One step at a time: a change while one runs is not started and the rows show what is in place again.
    private void Run(string what, Func<Task<SetupResult>> step)
    {
        if (_busy)
        {
            Show();
            return;
        }
        _busy = true;
        _ = Guard.RunAsync(what, async () =>
        {
            try
            {
                await _runSetup(Restarting, step());
            }
            finally
            {
                _busy = false;
                Show();
            }
        });
    }

    private void OptionsChanged()
    {
        foreach (var name in new[] { nameof(ShowsOptions), nameof(ShowsPick), nameof(CanPostMix), nameof(CanOriginalPreMix), nameof(CanOriginalPostMix), nameof(InstallPreMix),
                     nameof(InstallPostMix), nameof(UseOriginalPreMix), nameof(UseOriginalPostMix), nameof(AllowSilentBuffer), nameof(AutoAdjust), nameof(ModeText) })
            OnPropertyChanged(name);
    }

    public void Dispose() => _support.Changed -= Sync;
}
