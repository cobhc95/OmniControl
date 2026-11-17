using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Services;

namespace OmniControl.App.Pages;

/// <summary>
/// A card that states a fact instead of holding a setting: its name, the value (plain), a muted note, tags (<see cref="TagItem"/>) and
/// at most one pill that does something about it ("Open on GitHub"). Everything but the name may change while the page is open.
/// </summary>
public sealed partial class FactItem : ObservableObject, IEditorItem, IHalfWidth
{
    public string Label { get; }

    /// <summary>True: the card takes half the width and may share a row with the next half-width card.</summary>
    public bool Half { get; init; }

    [ObservableProperty] private string _value = string.Empty;

    /// <summary>A muted line after the value (when it was checked, what it means); wraps to a second line before the card grows.</summary>
    [ObservableProperty] private string _note = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasTags))] private IReadOnlyList<TagItem> _tags = Array.Empty<TagItem>();

    /// <summary>The words of the pill; empty: no pill.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasAction))] private string _actionText = string.Empty;

    public bool HasTags => Tags.Count > 0;

    public bool HasAction => ActionText.Length > 0;

    public IRelayCommand Command { get; }

    public FactItem(string label, Action? run = null)
    {
        Label = label;
        Command = new RelayCommand(() => run?.Invoke());
    }
}
