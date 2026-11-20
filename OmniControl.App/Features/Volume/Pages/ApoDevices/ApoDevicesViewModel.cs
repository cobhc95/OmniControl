using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Pages.ApoDevices;

/// OmniControl's own Equalizer APO device window, the one place it is installed or uninstalled: every playback and recording device with
/// its status from the last check (running only after a passed check) and Install or Uninstall, each confirmed inline under its row, then
/// done as Equalizer APO's Device Selector does (one prompt, an audio restart and every device it is on checked, <see cref="EngineInstaller"/>);
/// the first Install installs the copy itself. Details shows a device's record. "Troubleshooting options" shows Device Selector's
/// troubleshooting panel for the selected installed device (DeviceSelector.ui/.cpp: Install APO and Use original APO for pre-mix and
/// post-mix, Auto, the install mode, Allow silent buffer modification), each change putting it on again with that option. Reads the
/// effects support on open and whenever it changes (devices come or go), never by a timer.
public sealed partial class ApoDevicesViewModel : ObservableObject, IDisposable
{
    private const string Restarting = "Windows audio restarts for a moment…";

    private readonly EffectsSupportService _support;
    private readonly EngineInstaller _installer;
    private readonly MixerService _mixer;
    private readonly Func<string?, Task<SetupResult>, Task> _runSetup;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly ApoRowHost _host;
    private readonly List<ApoDeviceRow> _rows = new();
    private bool _busy;

    public ApoDevicesViewModel(EffectsSupportService support, EngineInstaller installer, MixerService mixer, Func<string?, Task<SetupResult>, Task> runSetup,
        Func<DialogMessage, Task<bool>> ask)
    {
        (_support, _installer, _mixer, _runSetup, _ask) = (support, installer, mixer, runSetup, ask);
        _host = new ApoRowHost(Pick, Ask, Confirm, row => _ = Guard.RunAsync("Equalizer APO: device details", () => DetailsAsync(row)));
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

    /// Device Selector's page 1 (the options) when an installed device is selected, else page 0 (its hint).
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

    private static string ModeName(ApoInstallMode mode) => $"Install as {ModeShort(mode)}";

    private static string ModeShort(ApoInstallMode mode) => mode switch
    {
        ApoInstallMode.LfxGfx => "LFX/GFX",
        ApoInstallMode.SfxMfx => "SFX/MFX",
        _ => "SFX/EFX",
    };

    /// Up/Down: the next or previous device, wrapping at both ends.
    public void Move(int direction)
    {
        if (_rows.Count == 0) return;
        var at = Selected is null ? (direction > 0 ? -1 : 0) : _rows.IndexOf(Selected);
        Pick(_rows[Wrap.Index(at + direction, _rows.Count)]);
    }

    /// Enter: opens the selected device's confirmation, or confirms it when open.
    public void ActOnSelected()
    {
        if (Selected is not { CanAct: true } row) return;
        if (row.IsConfirming) Confirm(row);
        else Ask(row, true);
    }

    /// Esc: closes an open confirmation; false when none was open (the window then closes).
    public bool CancelConfirm()
    {
        if (_rows.FirstOrDefault(r => r.IsConfirming) is not { } open) return false;
        open.IsConfirming = false;
        return true;
    }

    private void Pick(ApoDeviceRow row)
    {
        if (Selected is { } old) old.IsSelected = false;
        row.IsSelected = true;
        Selected = row;
        OptionsChanged();
    }

    /// One confirmation open at a time, under the row it is for.
    private void Ask(ApoDeviceRow row, bool open)
    {
        Pick(row);
        foreach (var r in _rows) r.IsConfirming = open && r == row && !_busy;
    }

    private void Confirm(ApoDeviceRow row)
    {
        row.IsConfirming = false;
        if (row.Entry is not { } e) return;
        var install = !e.Attached;
        Run("Equalizer APO: device " + (install ? "install" : "uninstall"), row,
            install ? ("Installing…", "Install · Restart audio · Check") : ("Uninstalling…", "Uninstall · Restart audio · Check the others"),
            () => install ? _installer.InstallOnAsync(e) : _installer.DetachAsync(e));
    }

    /// The device's record: what the last check tried and found. Changes nothing.
    private Task DetailsAsync(ApoDeviceRow row)
    {
        if (row.Entry is not { } e) return Task.CompletedTask;
        var check = e.LastCheck;
        var lines = new List<string> { $"Device: {e.Name}", $"Status: {row.Status}", $"Install mode now: {ModeShort(e.State.Mode)}" };
        lines.AddRange(check.Tries.Count == 0 ? new[] { "Install modes tried: none recorded" }
            : check.Tries.Select((t, i) => $"Try {i + 1}: {ModeShort(t.Mode)}, {ResultWords(t.Result)}"));
        lines.Add($"Last check: {(check.Result.Length == 0 ? "never" : ResultWords(check.Result))}{(check.At is { } at ? $", {at.ToLocalTime():g}" : string.Empty)}");
        if (ApoSetup.LastResult() is { } why) lines.Add($"Last step's problem: {why}");
        lines.Add($"Log: {EngineInstaller.LogPath}");
        var record = string.Join(Environment.NewLine, lines);
        return _ask(new DialogMessage($"Equalizer APO on {e.Name}", record, Details: record));
    }

    private static string ResultWords(string result) => result switch
    {
        ApoSetup.Running => "running",
        ApoSetup.OriginalFailed => "running, the device's own effects didn't start inside it",
        ApoSetup.Failed => "didn't start",
        ApoSetup.NeedsRestart => "waiting for an audio restart",
        _ => "not checked (disabled or unplugged)",
    };

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
        var wanted = apo.OwnDevices.Where(d => !d.Input).Concat(apo.OwnDevices.Where(d => d.Input)).ToList();
        if (!wanted.Select(w => w.Guid).SequenceEqual(_rows.Select(r => r.Key), StringComparer.OrdinalIgnoreCase))
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
                    var row = new ApoDeviceRow(w.Guid, _host);
                    _rows.Add(row);
                    Items.Add(row);
                    if (string.Equals(w.Guid, selected, StringComparison.OrdinalIgnoreCase)) Pick(row);
                }
            }
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            var (status, detail, tone) = StatusOf(wanted[i], defaults.Contains(wanted[i].Guid));
            _rows[i].Show(wanted[i], status, detail, tone);
            _rows[i].CanAct = true;
        }
        OptionsChanged();
    }

    /// One main line and one detail line from what is in place and the last check: "running" only after a passed check.
    private static (string Status, string Detail, ApoRowTone Tone) StatusOf(ApoDeviceEntry e, bool isDefault)
    {
        var facts = new List<string>();
        if (isDefault) facts.Add("Default device");
        if (e.Unplugged) facts.Add("Unplugged");
        if (e.Disabled) facts.Add("Disabled");
        if (!e.Attached) return ("Not installed", string.Join(" · ", facts), ApoRowTone.Plain);
        var check = e.LastCheck;
        if (check.Result is ApoSetup.Running or ApoSetup.OriginalFailed && !e.EnhancementsOff)
        {
            var good = new List<string> { ModeShort(e.State.Mode) };
            if (check.Result == ApoSetup.OriginalFailed) good.Add("device's own effects didn't start");
            if (check.At is { } at) good.Add($"checked {at.ToLocalTime():t}");
            return ("Installed · running", string.Join(" · ", good), ApoRowTone.Good);
        }
        var why = e.EnhancementsOff ? "Audio enhancements are off"
            : check.Tries.Count > 0 ? $"Tried {string.Join(", ", check.Tries.Select(t => ModeShort(t.Mode)))}"
            : check.Result == ApoSetup.NeedsRestart ? "Waits for an audio restart"
            : check.Result == ApoSetup.Failed ? "Check failed"
            : "Not checked";
        return ("Installed, not running", string.Join(" · ", facts.Prepend(why)), ApoRowTone.Bad);
    }

    /// A troubleshooting option of the selected device: put on again with it (Device Selector's reinstall; with neither APO ticked its
    /// install writes nothing, so the device ends up without Equalizer APO, as in Device Selector).
    private void Change(Func<ApoInstallState, ApoInstallState> edit)
    {
        if (Chosen is not { } e || Selected is not { } row) return;
        var state = edit(e.State);
        if (state != e.State) Run("Equalizer APO: device option", row, ("Installing again…", "Install · Restart audio · Check"), () => _installer.AttachAsync(e, state));
    }

    /// One step at a time: every row's buttons wait while it runs and the row shows it; a change while one runs is not started.
    private void Run(string what, ApoDeviceRow row, (string Status, string Steps) working, Func<Task<SetupResult>> step)
    {
        if (_busy)
        {
            Show();
            return;
        }
        _busy = true;
        foreach (var r in _rows) r.CanAct = false;
        row.Working(working.Status, working.Steps);
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
