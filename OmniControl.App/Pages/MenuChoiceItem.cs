using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Controls;

namespace OmniControl.App.Pages;

/// <summary>
/// A card whose choice is a small menu (the shared chip and <see cref="MenuRowList"/> pop-down) instead of a plain drop-down list: it can hold
/// parts a drop-down cannot, such as a thin line between groups, a muted heading, a way out to a manager, or a list where several rows are
/// ticked (the OSD's Position and Show on). The rows are asked every time the menu opens, and again after <see cref="Refresh"/>, so they
/// follow the settings; each row's own command does the choosing.
/// </summary>
public sealed partial class MenuChoiceItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Func<string> _words;
    private readonly Func<IReadOnlyList<MenuRowItem>> _rows;

    public string Label { get; }

    /// <summary>True: the card takes half the width and may share a row with the next half-width card.</summary>
    public bool Half { get; init; }

    /// <summary>A fact about the choice the label does not say, shown as the chip's tooltip; null: none.</summary>
    public string? Tip { get; init; }

    /// <summary>What the chip says: the choice now.</summary>
    [ObservableProperty] private string _text;

    /// <param name="words">The chip's words (the choice now).</param>
    /// <param name="rows">The menu's rows.</param>
    public MenuChoiceItem(string label, Func<string> words, Func<IReadOnlyList<MenuRowItem>> rows)
    {
        (Label, _words, _rows) = (label, words, rows);
        _text = words();
    }

    /// <summary>The menu's rows as they are now.</summary>
    public IReadOnlyList<MenuRowItem> Rows => _rows();

    /// <summary>Reads the chip's words and the rows again (the setting changed elsewhere, or by one of the rows).</summary>
    public void Refresh()
    {
        Text = _words();
        OnPropertyChanged(nameof(Rows));
    }
}
