using System.Collections.ObjectModel;

namespace OmniControl.App.Pages.Displays;

/// <summary>One detected display and the controls it actually supports.</summary>
public sealed class DisplayItem
{
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required ObservableCollection<SliderItem> Settings { get; init; }
    public bool HasSettings => Settings.Count > 0;
    public bool HasNoSettings => Settings.Count == 0;
}
