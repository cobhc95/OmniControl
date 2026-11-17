using OmniControl.App.Audio.Engine;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Controls;
using OmniControl.App.Dialogs;
using OmniControl.App.Pages;
using OmniControl.App.Services;
using OmniControl.Core.Models;

namespace OmniControl.App.Fx;

/// <summary>
/// A device folder as the FX window shows it (docs/mockups/fx-device-folders.html): the row's device, tag and count, and the editor on
/// the right: the device menu, the device's method (read only: the Mixer owns it), Mix and the effects inside. The slot stores the
/// device id and the device's name (kept for when it is unplugged).
/// </summary>
public sealed partial class FxFolderViewModel : ObservableObject
{
    private readonly FxSlot _slot;
    private readonly FxDeviceSource _devices;
    private readonly Action _save;
    private readonly Action _addHere;
    private readonly Action<FxSlotViewModel> _select;
    private readonly Func<Task> _installApo;
    private readonly Action _layout;
    private readonly MenuChoiceItem _deviceCard;
    private readonly FactItem _methodCard;

    internal FxFolderViewModel(FxSlot slot, FxDeviceSource devices, Action save, Action<string> openMixer, Action addHere, Action<FxSlotViewModel> select,
        Func<Task> installApo, Action layout, Action saveAsPreset)
    {
        _saveAsPreset = saveAsPreset;
        _installApo = installApo;
        _layout = layout;
        _slot = slot;
        _devices = devices;
        _save = save;
        _addHere = addHere;
        _select = select;
        _deviceCard = new MenuChoiceItem("Device", () => ChipText, DeviceRows) { Tip = "The playback device this folder is for" };
        _methodCard = new FactItem("Method", () => openMixer(_slot.DeviceId)) { ActionText = "Change in Mixer", ActionIsLink = true };
        _mixCard = new SliderItem("Mix", Mix, v => { Mix = v; return OperationResult.Ok("FX"); }, _ => { }, 0, Percent.Max, "%", (int)DefaultMix) { Thick = true };
        Cards = new object[] { _deviceCard, _methodCard, _mixCard };
        Refresh();
    }

    private readonly Action _saveAsPreset;
    private readonly SliderItem _mixCard;

    /// <summary>The editor's cards: the device menu, the device's method (read only) and Mix.</summary>
    public IReadOnlyList<object> Cards { get; }

    /// <summary>Whether the folder's effects show under it in the list; the chain hides their rows while it is closed.</summary>
    [ObservableProperty] private bool _expanded = true;

    partial void OnExpandedChanged(bool value) => _layout();

    /// <summary>The effects inside, in order (kept by the chain).</summary>
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

    /// <summary>
    /// Folder Mix, 0-100: how much of the folder's effects is heard against the sound that enters the folder. OmniControl's own engine
    /// mixes it (<c>MixCloseStage</c>); Equalizer APO has no dry/wet mix, so below 100 % it runs none of the folder's effects (said on the Method card).
    /// </summary>
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

    /// <summary>Reads the device's state again (devices, installs and the engine's status change it).</summary>
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
        RefreshMethodNote();
    }

    /// <summary>The Method card's muted line: when the device has no method yet, or that Equalizer APO cannot apply a Mix below 100 %.</summary>
    private void RefreshMethodNote() => _methodCard.Note =
        MethodTag is null ? "It chooses its method when it plays."
        : MethodTag.Text.StartsWith(FxDeviceSource.ApoTag, StringComparison.Ordinal) && Mix < FxSlot.DefaultWet ? "Equalizer APO has no mix: its effects do not run below 100 %."
        : string.Empty;

    /// <summary>The folder was changed from outside (a folder preset replaced its effects and Mix): the editor shows what the slot holds now.</summary>
    public void Reload()
    {
        _mixCard.Load(Mix);
        OnPropertyChanged(nameof(Mix));
        Refresh();
    }

    /// <summary>The device menu's rows: every playback device with its tags, the chosen one ticked.</summary>
    private IReadOnlyList<MenuRowItem> DeviceRows() => _devices.Devices()
        .Select(d => new MenuRowItem(d.Name, string.Empty, d.Id == _slot.DeviceId, d.Name, new RelayCommand(() => Choose(d)), d.Tags))
        .Prepend(MenuRowItem.Header("Playback devices"))
        .Append(new MenuRowItem("Install Equalizer APO on a device…", string.Empty, false, "Opens Equalizer APO's setup", new AsyncRelayCommand(_installApo), RuleAbove: true))
        .ToList();

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

/// <summary>What the FX window gives one chain (keeps <see cref="FxChainViewModel"/>'s constructor small).</summary>
/// <param name="Readings">What the effects engine measures for one of the chain's dynamics effects now.</param>
/// <param name="ManagePresets">Opens the shared preset manager at an effect kind's card.</param>
/// <param name="Devices">The playback devices, for the master's device folders; null for every other chain.</param>
/// <param name="Mixer">Where the Volume page's Mixer is opened and its Equalizer APO install step run (the Mixer owns each device's method).</param>
/// <param name="Ask">Asks a question through the program's dialogs (true = confirmed): removing a device folder that holds effects.</param>
/// <param name="SaveFolderPreset">Keeps a device folder as a preset and opens the preset manager at the folder presets.</param>
public sealed record FxChainTies(Func<FxSlot, DynamicsReading?> Readings, Action<FxKind> ManagePresets, FxDeviceSource? Devices, FxMixerLinks Mixer,
    Func<DialogMessage, Task<bool>> Ask, Action<FolderPreset> SaveFolderPreset);

/// <summary>The two things the FX window asks of the Volume page (given by the shell, so the FX window never knows the page).</summary>
public sealed record FxMixerLinks(Action<string> OpenMixer, Func<Task> InstallApo);
