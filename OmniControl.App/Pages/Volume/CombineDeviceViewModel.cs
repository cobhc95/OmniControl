using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Controls;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The Combine devices window: a name, the kind (speakers or microphones), the member devices (ticks), and whether a virtual cable is
/// free for it (with the setup step when not). Create is possible without a free cable: the new device then says what it needs. The same
/// window edits a combined device (its kind stays; members that are unplugged now stay members).
/// </summary>
public sealed partial class CombineDeviceViewModel : ObservableObject
{
    private const int MinMembers = 2;

    private readonly IReadOnlyList<AudioDevice> _speakers;
    private readonly IReadOnlyList<AudioDevice> _microphones;
    private readonly List<string> _ticked = new(); // in the order ticked

    /// <param name="speakers">Playback devices that can be members (no cables).</param>
    /// <param name="microphones">Recording devices that can be members (no cables).</param>
    /// <param name="cableStatus">One line: the cable it gets or uses, or what it waits for.</param>
    /// <param name="cableSetup">The step that gets a cable (installer or VB-Audio's page); null = nothing to get.</param>
    /// <param name="editing">The combined device to edit; null = a new one.</param>
    public CombineDeviceViewModel(IReadOnlyList<AudioDevice> speakers, IReadOnlyList<AudioDevice> microphones, string cableStatus,
        ModeCardAction? cableSetup, CombinedDevice? editing = null)
    {
        _speakers = speakers;
        _microphones = microphones;
        CableStatus = cableStatus;
        CableSetup = cableSetup;
        IsEditing = editing is not null;
        Members = new OptionListModel(Toggle);
        if (editing is not null)
        {
            Name = editing.Name;
            IsMicrophones = editing.Kind == CombinedKind.Microphones; // before the members: a change of kind clears them
            _ticked.AddRange(editing.Members);
        }
        Fill();
    }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanCreate))] private string _name = string.Empty;

    /// <summary>True: combining microphones; false: speakers.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsSpeakers))] private bool _isMicrophones;

    public bool IsSpeakers => !IsMicrophones;

    /// <summary>Editing an existing device: its kind can't change, and the button saves.</summary>
    public bool IsEditing { get; }

    public string Heading => IsEditing ? "Edit combined device" : "Combine devices";
    public string ConfirmText => IsEditing ? "Save" : "Create";

    public OptionListModel Members { get; }

    public string CableStatus { get; }
    public ModeCardAction? CableSetup { get; }
    public bool HasCableSetup => CableSetup is not null;

    /// <summary>A name and at least two devices.</summary>
    public bool CanCreate => Name.Trim().Length > 0 && _ticked.Count >= MinMembers;

    [RelayCommand] private void ChooseSpeakers() => IsMicrophones = false;
    [RelayCommand] private void ChooseMicrophones() => IsMicrophones = true;

    partial void OnIsMicrophonesChanged(bool value)
    {
        _ticked.Clear();
        Fill();
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
    }

    private void Fill() =>
        Members.SetItems((IsMicrophones ? _microphones : _speakers).Select(d => new OptionEntry(d.Id, d.Name) { IsChecked = _ticked.Contains(d.Id) }));
}
