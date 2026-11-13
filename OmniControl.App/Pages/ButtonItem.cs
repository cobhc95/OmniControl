using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Pages;

/// <summary>
/// A card with one button that does something once (turn the screen off, reset a picture, remove a folder). Not a setting, so it has
/// no value; its description may change (what the last press did).
/// </summary>
public sealed partial class ButtonItem : ObservableObject, IEditorItem, IHalfWidth
{
    public string Label { get; }

    /// <summary>True: the card takes half the width and may share a row with the next half-width card; the description is then only a tooltip.</summary>
    public bool Half { get; init; }

    /// <summary>The text on the button.</summary>
    public string ButtonText { get; }

    /// <summary>A short line beside the label saying what the button does or how to undo it (or what it last did); also its tooltip.</summary>
    [ObservableProperty] private string _description;

    public IRelayCommand Command { get; }

    public Availability Availability { get; } = new();

    public ButtonItem(string label, string buttonText, string description, Action run)
    {
        Label = label;
        ButtonText = buttonText;
        _description = description;
        Command = new RelayCommand(run);
    }
}
