namespace OmniControl.App.Features.Volume.Pages;

/// The "+ This device is a cable..." pop-down of the Virtual cables list: one playback device (where sound is sent) and one recording
/// device (where the same sound comes out), a name, Add. Offers only devices OmniControl does not already know as a cable end; the page
/// stores the pair (<see cref="Audio.MixerService.MarkCable"/>).
public sealed partial class CableMarkMenu : ObservableObject
{
    public const string Label = "+ This device is a cable...", LabelTip = "Mark a playback device and a recording device that carry the same sound as one cable";

    private readonly Action<string, string, string> _mark;
    private readonly Action<CableMarkMenu> _opening;
    private IReadOnlyList<AudioDevice> _playback = Array.Empty<AudioDevice>();
    private IReadOnlyList<AudioDevice> _recording = Array.Empty<AudioDevice>();
    /// The name filled in from the playback device, so a pick of another device renames it until the user types one.
    private string _suggested = string.Empty;

    /// <param name="mark">Stores a pair: its name, the playback device's id, the recording device's id.</param>
    /// <param name="opening">The pop-down is opening: the page calls <see cref="Refresh"/> with the devices plugged in now.</param>
    public CableMarkMenu(Action<string, string, string> mark, Action<CableMarkMenu> opening)
    {
        _mark = mark;
        _opening = opening;
        Playback = new ChoiceItem("Sound goes into", Array.Empty<string>(), string.Empty, _ => SuggestName());
        Recording = new ChoiceItem("Comes out of", Array.Empty<string>(), string.Empty, _ => AddCommand.NotifyCanExecuteChanged());
    }

    public ChoiceItem Playback { get; }
    public ChoiceItem Recording { get; }

    [ObservableProperty, NotifyCanExecuteChangedFor(nameof(AddCommand))] private string _name = string.Empty;

    /// There is at least one device of each kind to choose.
    [ObservableProperty] private bool _hasDevices;

    /// The pop-down is opening.
    public void Open() => _opening(this);

    /// The devices that are not known cable ends now, the first of each chosen.
    public void Refresh(IReadOnlyList<AudioDevice> playback, IReadOnlyList<AudioDevice> recording)
    {
        _playback = playback;
        _recording = recording;
        Playback.Load(playback.Select(d => d.Name).ToList(), playback.FirstOrDefault()?.Name ?? string.Empty);
        Recording.Load(recording.Select(d => d.Name).ToList(), recording.FirstOrDefault()?.Name ?? string.Empty);
        HasDevices = playback.Count > 0 && recording.Count > 0;
        _suggested = string.Empty;
        Name = string.Empty;
        SuggestName();
    }

    private void SuggestName()
    {
        if (Name.Length > 0 && Name != _suggested) return;
        Name = _suggested = Shared.Services.DeviceNames.Short(Playback.Selected);
        AddCommand.NotifyCanExecuteChanged();
    }

    private bool CanAdd() => Name.Trim().Length > 0 && Find(_playback, Playback.Selected) is not null && Find(_recording, Recording.Selected) is not null;

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        if (Find(_playback, Playback.Selected) is not { } p || Find(_recording, Recording.Selected) is not { } r) return;
        _mark(Name.Trim(), p.Id, r.Id);
    }

    private static AudioDevice? Find(IReadOnlyList<AudioDevice> devices, string name) => devices.FirstOrDefault(d => d.Name == name);
}
