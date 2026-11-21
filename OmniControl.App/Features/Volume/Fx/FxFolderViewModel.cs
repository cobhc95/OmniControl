using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Fx;

/// A device folder as the FX window shows it: the row's device, tag and count, and the editor on
/// the right: the device menu, the device's method (read only: the Mixer owns it), Mix and the effects inside. The slot stores the
/// device id and the device's name (kept for when it is unplugged).
public sealed partial class FxFolderViewModel : ObservableObject
{
    private readonly FxSlot _slot;
    private readonly FxDeviceSource _devices;
    private readonly Action _save;
    private readonly Action _addHere;
    private readonly Action<FxSlotViewModel> _select;
    private readonly FxMixerLinks _mixer;
    private readonly Action _layout;
    private readonly MenuChoiceItem _deviceCard;
    private readonly FactItem _methodCard;

    internal FxFolderViewModel(FxSlot slot, FxDeviceSource devices, Action save, FxMixerLinks mixer, Action addHere, Action<FxSlotViewModel> select,
        Action layout, Action saveAsPreset)
    {
        _saveAsPreset = saveAsPreset;
        _mixer = mixer;
        _layout = layout;
        _slot = slot;
        _devices = devices;
        _save = save;
        _addHere = addHere;
        _select = select;
        _deviceCard = new MenuChoiceItem("Device", () => ChipText, DeviceRows) { Tip = "The playback device this folder is for" };
        _methodCard = new FactItem("Method", () => mixer.OpenMixer(_slot.DeviceId)) { ActionText = "Change in Mixer", ActionIsLink = true };
        _mixCard = new SliderItem("Mix", Mix, v => { Mix = v; return OperationResult.Ok("FX"); }, _ => { }, 0, Percent.Max, "%", (int)DefaultMix);
        Cards = new object[] { _deviceCard, _methodCard, _mixCard };
        Refresh();
    }

    private readonly Action _saveAsPreset;
    private readonly SliderItem _mixCard;

    /// The editor's cards: the device menu, the device's method (read only) and Mix.
    public IReadOnlyList<object> Cards { get; }

    /// Whether the folder's effects show under it in the list; the chain hides their rows while it is closed.
    [ObservableProperty] private bool _expanded = true;

    partial void OnExpandedChanged(bool value) => _layout();

    /// The effects inside, in order (kept by the chain).
    public ObservableCollection<FxSlotViewModel> Children { get; } = new();

    public string DeviceName => _slot.Name;
    public string ChipText => _slot.Name;
    [ObservableProperty] private bool _playing;
    [ObservableProperty] private TagItem _rowTag = new(string.Empty);
    [ObservableProperty] private TagItem? _methodTag;
    [ObservableProperty] private bool _isCable;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEmpty))] private int _count;

    public bool IsEmpty => Count == 0;

    public bool Dim => !Playing;

    partial void OnPlayingChanged(bool value) => OnPropertyChanged(nameof(Dim));

    /// Folder Mix, 0-100: how much of the folder's effects is heard against the sound that enters the folder (dry x (1 - mix) +
    /// processed x mix). OmniControl's own engine mixes it (<c>MixCloseStage</c>), Equalizer APO with virtual channels (<c>ApoChainWriter</c>);
    /// an Equalizer APO too old for that keeps it at 100 % and says so (<see cref="FxDeviceSource.MixBlockedOn"/>).
    public int Mix
    {
        get => _slot.Wet;
        set
        {
            value = Percent.Clamp(value);
            if (_slot.Wet == value) return;
            _slot.Wet = value;
            OnPropertyChanged();
            RefreshMethodNote();
            _save();
        }
    }

    public double DefaultMix => FxSlot.DefaultWet;

    /// Reads the device's state again (devices, installs and the engine's status change it).
    public void Refresh()
    {
        var device = _devices.Find(_slot.DeviceId, _slot.Name);
        Playing = device.Playing;
        RowTag = _devices.RowTag(device);
        MethodTag = _devices.MethodTag(device);
        IsCable = RowTag.IsCable;
        OnPropertyChanged(nameof(DeviceName));
        OnPropertyChanged(nameof(ChipText));
        _deviceCard.Refresh();
        _methodCard.Tags = MethodTag is { } tag ? new[] { tag } : Array.Empty<TagItem>();
        RefreshMix();
        RefreshMethodNote();
    }

    /// Mix works, or is held at 100 % with its one sentence while the device's Equalizer APO can't blend.
    private void RefreshMix()
    {
        if (_devices.MixBlockedOn(_slot.DeviceId) is not { } reason)
        {
            _mixCard.Availability.MakeAvailable();
            return;
        }
        _mixCard.Availability.MakeUnavailable(reason);
        if (Mix == FxSlot.DefaultWet) return;
        Mix = FxSlot.DefaultWet;
        _mixCard.Load(Mix);
    }

    /// The Method card's muted line: when the device has no method yet, or that its Equalizer APO keeps Mix at 100 %.
    private void RefreshMethodNote() => _methodCard.Note =
        MethodTag is null ? "It chooses its method when it plays."
        : _devices.MixBlockedOn(_slot.DeviceId) ?? string.Empty;

    /// The folder was changed from outside (a folder preset replaced its effects and Mix): the editor shows what the slot holds now.
    public void Reload()
    {
        _mixCard.Load(Mix);
        OnPropertyChanged(nameof(Mix));
        Refresh();
    }

    /// The device menu's rows: every playback device with its tags, the chosen one ticked.
    private IReadOnlyList<MenuRowItem> DeviceRows()
    {
        var (label, tip) = _mixer.ApoStepText();
        return _devices.Devices()
            .Select(d => new MenuRowItem(d.Name, string.Empty, d.Id == _slot.DeviceId, string.Empty, new RelayCommand(() => Choose(d)), d.Tags))
            .Prepend(MenuRowItem.Header("Playback devices"))
            .Append(new MenuRowItem(label, string.Empty, false, tip, new AsyncRelayCommand(_mixer.ApoStep), RuleAbove: true))
            .ToList();
    }

    private void Choose(FolderDevice device)
    {
        _slot.DeviceId = device.Id;
        _slot.Name = device.Name;
        Refresh();
        _save();
    }

    [RelayCommand] private void ToggleExpanded() => Expanded = !Expanded;
    [RelayCommand] private void AddHere() => _addHere();
    [RelayCommand] private void SaveAsPreset() => _saveAsPreset();
    [RelayCommand] private void SelectChild(FxSlotViewModel child) => _select(child);
}

/// What the FX window gives one chain (keeps <see cref="FxChainViewModel"/>'s constructor small).
/// <param name="Readings">What the effects engine measures for one of the chain's dynamics effects now.</param>
/// <param name="ManagePresets">Opens the shared preset manager at an effect kind's card.</param>
/// <param name="Spectrum">Opens the live spectrum of an equaliser of this chain until the result is disposed (its pictures go to the callback on the UI thread).</param>
/// <param name="Devices">The playback devices, for the master's device folders; null for every other chain.</param>
/// <param name="Mixer">Where the Volume page's Mixer is opened and its Equalizer APO install step run (the Mixer owns each device's method).</param>
/// <param name="Ask">Asks a question through the program's dialogs (true = confirmed): removing a device folder that holds effects.</param>
/// <param name="SaveFolderPreset">Keeps a device folder as a preset and opens the preset manager at the folder presets.</param>
/// <param name="View">How the FX windows show their effects' editors (app settings).</param>
public sealed record FxChainTies(Func<FxSlot, DynamicsReading?> Readings, Func<FxSlot, Action<float[]?>, IDisposable> Spectrum, Action<FxKind> ManagePresets, FxDeviceSource? Devices, FxMixerLinks Mixer,
    Func<DialogMessage, Task<bool>> Ask, Action<FolderPreset> SaveFolderPreset, FxViewSettings View);

/// What the FX window asks of the Volume page (given by the shell, so the FX window never knows the page): open its Mixer at a device,
/// and its one Equalizer APO step (its Device Selector when it is installed, else its installer) with that step's words and tooltip.
public sealed record FxMixerLinks(Action<string> OpenMixer, Func<Task> ApoStep, Func<(string Label, string Tip)> ApoStepText);
