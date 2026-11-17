using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Services;

namespace OmniControl.App.Pages;

/// <summary>
/// A card that states a fact instead of holding a setting: its name, the value (plain), a muted note, tags (<see cref="TagItem"/>) and
/// at most one pill that does something about it ("Open on GitHub"). Everything but the name may change while the page is open.
/// </summary>
public sealed partial class FactItem : ObservableObject, IEditorItem, IHalfWidth
{
    public string Label { get; }

    /// <summary>An icon id of the icon library drawn before the label (Other PCs' mirrored cards); null: none.</summary>
    public string? Icon { get; init; }

    /// <summary>True: the card takes half the width and may share a row with the next half-width card.</summary>
    public bool Half { get; init; }

    /// <summary>True: the card is laid out as a media line: the icon, the value (semi-bold) over the muted note, then the <see cref="Pills"/> at the right (Other PCs' Now playing); the label is not shown.</summary>
    public bool Stacked { get; init; }

    public bool IsPlain => !Stacked;

    /// <summary>Pills at the right of a stacked card (shared <see cref="ChoiceChips"/>); empty: none.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPills))] private IReadOnlyList<ChipItem> _pills = Array.Empty<ChipItem>();

    public bool HasPills => Pills.Count > 0;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasValue))] private string _value = string.Empty;

    /// <summary>A muted line after the value (when it was checked, what it means); wraps to a second line before the card grows.</summary>
    [ObservableProperty] private string _note = string.Empty;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasTags))] private IReadOnlyList<TagItem> _tags = Array.Empty<TagItem>();

    /// <summary>The words of the pill; empty: no pill.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasAction), nameof(HasPillAction), nameof(HasLinkAction))] private string _actionText = string.Empty;

    public bool HasValue => Value.Length > 0;

    public bool HasTags => Tags.Count > 0;

    public bool HasAction => ActionText.Length > 0;

    /// <summary>True: the action is words inside the line (<c>Button.link</c>), not the accent pill (the device folder editor's "Change in Mixer").</summary>
    public bool ActionIsLink { get; init; }

    public bool HasPillAction => HasAction && !ActionIsLink;
    public bool HasLinkAction => HasAction && ActionIsLink;

    public IRelayCommand Command { get; }

    public FactItem(string label, Action? run = null)
    {
        Label = label;
        Command = new RelayCommand(() => run?.Invoke());
    }
}
