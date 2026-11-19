namespace OmniControl.App.Shared.Ui.Controls;

/// One colour of a colour pop-down (<c>Button.swatch</c> + <c>Ellipse.swatch</c>, drawn by <see cref="SwatchList"/>): the screenshot overlay's
/// drawing colours, the gadget's warn and icon colours. A null <see cref="Brush"/> is the "follow the text colour" dot (a dashed ring with an A).
public sealed record SwatchItem(IBrush? Brush, IRelayCommand Pick, string Tip = "")
{
    public bool Follows => Brush is null;
}

/// THE row of colour dots (<see cref="SwatchItem"/>s, wrapping; its panel and dot template are <c>ItemsControl.swatches</c> in Bars.axaml, a
/// class it gives itself because a type selector matches the style key, which is ItemsControl); <see cref="Picked"/> follows a dot's click
/// so a pop-down can close.
public sealed class SwatchList : ItemsControl
{
    public SwatchList()
    {
        Classes.Add("swatches");
        AddHandler(Button.ClickEvent, (_, e) => Picked?.Invoke(this, e));
    }

    protected override Type StyleKeyOverride => typeof(ItemsControl); // so ItemsControl's theme draws it

    public event EventHandler<RoutedEventArgs>? Picked;
}
