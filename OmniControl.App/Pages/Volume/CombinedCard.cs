using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// One combined device in the Wiring tab's "Combined devices" section: its name and members ("Stage speakers = Interface 1 +
/// Interface 2"), its cable state with the step that gets a cable when one is missing, Edit (the Combine devices window) and remove
/// (the Mixer row's ×, which asks first). The source is the same <see cref="VolumeRow"/> the Mixer shows.
/// </summary>
public sealed partial class CombinedCard : ObservableObject, IEditorItem
{
    private readonly Action _edit;

    public CombinedCard(VolumeRow source, Action edit)
    {
        Source = source;
        _edit = edit;
    }

    string IEditorItem.Label => Source.Name;

    public VolumeRow Source { get; }

    public string Key => Source.Key;

    [ObservableProperty] private string _membersText = string.Empty;

    [ObservableProperty] private CableState _cable = CableState.None;

    /// <summary>Install VB-CABLE or More cables, while the device waits for a cable; null = nothing to get.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasCableAction))] private ModeCardAction? _cableAction;

    public bool HasCableAction => CableAction is not null;

    [RelayCommand] private void Edit() => _edit();
}
