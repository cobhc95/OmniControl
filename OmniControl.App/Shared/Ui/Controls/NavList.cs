using Avalonia.Controls.Primitives;

namespace OmniControl.App.Shared.Ui.Controls;

/// The sidebar list (<c>ListBox.nav</c>): its <see cref="NavHeading"/> items get a container that cannot be clicked or focused, so
/// the arrow keys and the mouse treat them as if they were not there. Pages are ordinary items.
public sealed class NavList : ListBox
{
    protected override Type StyleKeyOverride => typeof(ListBox);

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        var heading = item is NavHeading;
        container.Focusable = !heading;
        container.IsHitTestVisible = !heading;
        container.Classes.Set("navheading", heading);
    }

    protected override void ClearContainerForItemOverride(Control container)
    {
        base.ClearContainerForItemOverride(container);
        container.Classes.Set("navheading", false);
    }
}
