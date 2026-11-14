using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Controls;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume;

/// <summary>How a new combined device starts: its kind (and whether the window hides the choice), the devices already ticked, its name.</summary>
public sealed record CombineStart(CombinedKind Kind, bool KindFixed, IReadOnlyList<string> Ticked, string Name);

/// <summary>
/// The window's cable line: the cable the device gets or uses, or what it waits for; the step that gets a cable (installer or VB-Audio's
/// page); and the Switch to the mode that allows it, e.g. "Switch to Fast" when the master bus holds the cable (null = not shown).
/// <see cref="Blocks"/> = a new device can't be created now (the text says why): Create stays dimmed.
/// </summary>
public sealed record CombineCableLine(string Text, ModeCardAction? Setup, ModeGate? Switch, bool Blocks = false);

/// <summary>
/// The Combine devices window: a name, the kind (speakers or microphones; hidden when the window was opened for one kind), a one-line
/// hint of what the kind does, the member devices (ticks), and the cable line. Create is dimmed, with the reason in the cable line, while
/// a new device of that kind can't work (Fast mode for speakers, no cable or none free: <see cref="SoundModes.CombineGate"/>). The same
/// window edits a combined device (its kind stays; members that are unplugged now stay members; saving is never blocked).
/// </summary>
public sealed partial class CombineDeviceViewModel : ObservableObject
{
    private const int MinMembers = 2;
    private const string SpeakersHint = "Plays the same sound on all of them.";

    private readonly IReadOnlyList<AudioDevice> _speakers;
    private readonly IReadOnlyList<AudioDevice> _microphones;
    private readonly Func<bool, CombineCableLine> _cableLine;
    private readonly Func<bool, Gate> _createGate;
    private readonly string? _recordingName;
    private readonly List<string> _ticked = new(); // in the order ticked

    /// <param name="speakers">Playback devices that can be members (no cables).</param>
    /// <param name="microphones">Recording devices that can be members (no cables).</param>
    /// <param name="cableLine">The cable line for microphones (true) or speakers (false), asked again when the kind or the mode changes.</param>
    /// <param name="createGate">Whether a new device of the kind (true = microphones) can be created now; a kind that can't is dimmed with the reason.</param>
    /// <param name="recordingName">What apps would pick as the combined microphone ("CABLE Output"); null = not known yet.</param>
    /// <param name="editing">The combined device to edit; null = a new one.</param>
    /// <param name="start">How a new one starts; null = speakers, nothing ticked.</param>
    public CombineDeviceViewModel(IReadOnlyList<AudioDevice> speakers, IReadOnlyList<AudioDevice> microphones, Func<bool, CombineCableLine> cableLine,
        Func<bool, Gate> createGate, string? recordingName, CombinedDevice? editing = null, CombineStart? start = null)
    {
        _speakers = speakers;
        _microphones = microphones;
        _cableLine = cableLine;
        _createGate = createGate;
        _recordingName = recordingName;
        IsEditing = editing is not null;
        KindFixed = start?.KindFixed == true;
        Members = new OptionListModel(Toggle);
        if (editing is not null)
        {
            Name = editing.Name;
            IsMicrophones = editing.Kind == CombinedKind.Microphones; // before the members: a change of kind clears them
            _ticked.AddRange(editing.Members);
        }
        else if (start is not null)
        {
            Name = start.Name;
            IsMicrophones = start.Kind == CombinedKind.Microphones;
            _ticked.AddRange(start.Ticked.Distinct());
        }
        Fill();
        RefreshCable();
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCreate))] private string _name = string.Empty;

    /// <summary>True: combining microphones; false: speakers.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSpeakers), nameof(Hint), nameof(TickNote), nameof(HasTickNote))] private bool _isMicrophones;

    public bool IsSpeakers => !IsMicrophones;

    /// <summary>Editing an existing device: its kind can't change, and the button saves.</summary>
    public bool IsEditing { get; }

    /// <summary>Opened for one kind (a table's "+ Combine speakers" or "+ Combine microphones", a microphone's "Combine with"): no kind choice.</summary>
    public bool KindFixed { get; }

    public bool ShowsKind => !IsEditing && !KindFixed;

    /// <summary>Why a new device of that kind can't be created now (its choice is dimmed); null = it can. Asked again with the cable line.</summary>
    public string? SpeakersReason => _createGate(false).Reason;
    public string? MicrophonesReason => _createGate(true).Reason;
    public bool CanChooseSpeakers => SpeakersReason is null;
    public bool CanChooseMicrophones => MicrophonesReason is null;

    public string Heading => IsEditing ? "Edit combined device" : !KindFixed ? "Combine devices" : IsMicrophones ? "Combine microphones" : "Combine speakers";
    public string ConfirmText => IsEditing ? "Save" : "Create";

    /// <summary>What the kind does, in one line ("Mixes them into one microphone. Apps record it from CABLE Output.").</summary>
    public string Hint => IsMicrophones
        ? _recordingName is null ? "Mixes them into one microphone." : $"Mixes them into one microphone. {SoundModes.AppsRecordFrom(_recordingName)}."
        : SpeakersHint;

    /// <summary>Why Create is dimmed for want of ticks ("Tick at least one more microphone"); empty when there are enough.</summary>
    public string TickNote
    {
        get
        {
            var noun = IsMicrophones ? "microphone" : "device";
            return _ticked.Count >= MinMembers ? string.Empty
                : _ticked.Count == MinMembers - 1 ? $"Tick at least one more {noun}"
                : $"Tick at least {MinMembers} {noun}s";
        }
    }

    public bool HasTickNote => TickNote.Length > 0;

    public OptionListModel Members { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCableSetup), nameof(HasCableSwitch), nameof(CanCreate))] private CombineCableLine _cable = new(string.Empty, null, null);

    public bool HasCableSetup => Cable.Setup is not null;
    public bool HasCableSwitch => Cable.Switch?.CanSwitch == true;

    /// <summary>A name, at least two devices, and (for a new device) a kind that can work now.</summary>
    public bool CanCreate => Name.Trim().Length > 0 && _ticked.Count >= MinMembers && (IsEditing || !Cable.Blocks);

    [RelayCommand] private void ChooseSpeakers() { if (CanChooseSpeakers) IsMicrophones = false; }
    [RelayCommand] private void ChooseMicrophones() { if (CanChooseMicrophones) IsMicrophones = true; }

    /// <summary>Asks the cable line again (the kind or the sound mode changed).</summary>
    public void RefreshCable()
    {
        Cable = _cableLine(IsMicrophones);
        OnPropertyChanged(nameof(SpeakersReason));
        OnPropertyChanged(nameof(MicrophonesReason));
        OnPropertyChanged(nameof(CanChooseSpeakers));
        OnPropertyChanged(nameof(CanChooseMicrophones));
    }

    partial void OnIsMicrophonesChanged(bool value)
    {
        _ticked.Clear();
        Fill();
        RefreshCable();
        OnPropertyChanged(nameof(CanCreate));
    }

    /// <summary>The device to make from what was filled in.</summary>
    public CombinedDevice Result() => new()
    {
        Name = Name.Trim(),
        Kind = IsMicrophones ? CombinedKind.Microphones : CombinedKind.Speakers,
        Members = _ticked.ToList(),
    };

    private void Toggle(OptionEntry entry)
    {
        if (!_ticked.Remove(entry.Id)) _ticked.Add(entry.Id);
        entry.IsChecked = _ticked.Contains(entry.Id);
        OnPropertyChanged(nameof(CanCreate));
        OnPropertyChanged(nameof(TickNote));
        OnPropertyChanged(nameof(HasTickNote));
    }

    private void Fill() =>
        Members.SetItems((IsMicrophones ? _microphones : _speakers).Select(d => new OptionEntry(d.Id, d.Name) { IsChecked = _ticked.Contains(d.Id) }));
}
