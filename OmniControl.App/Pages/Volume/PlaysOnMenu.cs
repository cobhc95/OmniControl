using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages.Volume;

/// <summary>
/// The one "Plays on" menu of a row, shared by both tabs (the Wiring cell's menu and the Mixer row's Plays on chip; one view,
/// <see cref="PlaysOnMenuView"/>): a title, the device entries (<see cref="WiringChoice"/>: what can be chosen, the current ones marked,
/// the rest dimmed with the reason), and, while the row takes one device only, the gate's note. The page fills it as it
/// opens, from the one routing model (<c>VolumePageViewModel.FillPlaysOn</c>); this class keeps no routing of its own.
/// </summary>
public sealed partial class PlaysOnMenu : ObservableObject
{
    private readonly Action<PlaysOnMenu> _opening;

    /// <param name="gate">Why the row can't take several devices (its note); open = it can.</param>
    /// <param name="opening">The menu is opening: the page fills <see cref="Choices"/> from what is plugged in now.</param>
    /// <param name="marksCurrent">The menu lists the devices in use too, ticked (the Mixer's chip: a pick replaces them); false = it lists only what can be added (the Wiring cell, whose chips show the rest).</param>
    public PlaysOnMenu(ControlGate gate, Action<PlaysOnMenu> opening, bool marksCurrent)
    {
        Gate = gate;
        _opening = opening;
        MarksCurrent = marksCurrent;
    }

    public ControlGate Gate { get; }

    /// <summary>The devices in use are listed and ticked; a pick makes the device the only one.</summary>
    public bool MarksCurrent { get; }

    /// <summary>The menu's title ("Plays on", "Add a device", "Choose a device").</summary>
    [ObservableProperty] private string _title = string.Empty;

    /// <summary>The menu's entries (filled when it opens).</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNoChoices))] private IReadOnlyList<WiringChoice> _choices = Array.Empty<WiringChoice>();

    public bool HasNoChoices => Choices.Count == 0;

    /// <summary>The menu is opening.</summary>
    public void Open() => _opening(this);
}
