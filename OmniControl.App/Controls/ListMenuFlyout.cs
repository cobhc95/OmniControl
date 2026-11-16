using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;

namespace OmniControl.App.Controls;

/// <summary>
/// THE narrow menu pop-down (<c>FlyoutPresenter.listmenu</c>): the classes, the offset below its opener and the rule that it slides
/// sideways, flips above and shrinks when the window is tight, set once so no site retypes them. Only the placement and the
/// opened/closed events are the site's own.
/// </summary>
public class ListMenuFlyout : Flyout
{
    public ListMenuFlyout()
    {
        FlyoutPresenterClasses.Add("listmenu");
        PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.SlideX
            | PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.ResizeY;
        if (Application.Current?.TryFindResource("MenuOffset", out var offset) == true && offset is double d) VerticalOffset = d;
    }
}
