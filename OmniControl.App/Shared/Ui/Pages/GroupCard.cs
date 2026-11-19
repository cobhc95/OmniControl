namespace OmniControl.App.Shared.Ui.Pages;

/// THE group card (look in Styles/Theme.axaml): a rounded panel with the group's header line (<see cref="GroupHeader"/>), a row of
/// <see cref="Tools"/> at the header's right end (shown only while the group is open, always the same spacing) and the body
/// (<see cref="ContentControl.Content"/>, shown only while the group is open). Every page's group is this one control.
public class GroupCard : ContentControl
{
    public static readonly StyledProperty<CardGroup?> GroupProperty = AvaloniaProperty.Register<GroupCard, CardGroup?>(nameof(Group));

    public static readonly DirectProperty<GroupCard, Avalonia.Controls.Controls> ToolsProperty =
        AvaloniaProperty.RegisterDirect<GroupCard, Avalonia.Controls.Controls>(nameof(Tools), c => c.Tools);

    /// The group the card shows: its name, summary, collapse state and Restore.
    public CardGroup? Group { get => GetValue(GroupProperty); set => SetValue(GroupProperty, value); }

    /// The controls at the right end of the header line (a preset picker, an Apps chip, a pill).
    public Avalonia.Controls.Controls Tools { get; } = new();
}
