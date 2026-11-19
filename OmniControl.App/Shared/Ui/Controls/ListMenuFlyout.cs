using System.ComponentModel;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;

namespace OmniControl.App.Shared.Ui.Controls;

/// THE menu pop-down (<c>FlyoutPresenter.listmenu</c>, also under <c>optionmenu</c>): the classes, the gap to its opener and the rule that it
/// slides sideways, set once so no site retypes them. Only the placement and the opened/closed events are the site's own.
/// The presenter keeps see-through room around it for its shadow (MenuShadowRoom); the offsets take that room off, so the visible menu sits
/// where the placement says. A pop-down placed above or below its opener opens directly under (or over) it, left edges aligned and at least
/// as wide as the opener, and picks its side from the room in the screen's working area (the taskbar excluded), every time its size
/// changes: the site's side when it fits, else the other side when that fits, else the roomier side, shrunk to it with a scroll bar.
/// Avalonia's own FlipY only flips when the other side fits whole, and its ResizeY does not shrink a pop-down that sizes to its content,
/// so a tall pop-down opened low ran under the taskbar.
public class ListMenuFlyout : Flyout
{
    private const PopupPositionerConstraintAdjustment Adjustment = PopupPositionerConstraintAdjustment.SlideX
        | PopupPositionerConstraintAdjustment.FlipY | PopupPositionerConstraintAdjustment.ResizeY;

    // The site's above/below placement while open (the flyout's own Placement is Custom then, so Place decides the side).
    private PlacementMode? _wanted;
    private readonly double _gap;
    private readonly Thickness _room;

    public ListMenuFlyout()
    {
        FlyoutPresenterClasses.Add("listmenu");
        PlacementConstraintAdjustment = Adjustment;
        if (Application.Current?.TryFindResource("MenuOffset", out var offset) == true && offset is double d) _gap = d;
        if (Application.Current?.TryFindResource("MenuShadowRoom", out var room) == true && room is Thickness t) _room = t;
        VerticalOffset = -_room.Top;
        CustomPopupPlacementCallback = Place;
    }

    protected override void OnOpening(CancelEventArgs args)
    {
        base.OnOpening(args);
        // A pointer or side placement: the visible menu's corner at the spot, or its edge against the opener's side.
        HorizontalOffset = Placement is PlacementMode.Left or PlacementMode.LeftEdgeAlignedTop or PlacementMode.LeftEdgeAlignedBottom ? _room.Right : -_room.Left;
        if (args.Cancel || _wanted is not null || !IsAboveOrBelow(Placement)) return;
        _wanted = Placement;
        Placement = PlacementMode.Custom;
        Popup.Child?.ClearValue(Control.MaxHeightProperty);
        Popup.Child?.ClearValue(Control.MinWidthProperty);
    }

    protected override void OnClosed()
    {
        if (_wanted is { } wanted)
        {
            _wanted = null;
            Placement = wanted;
        }
        base.OnClosed();
    }

    private void Place(CustomPopupPlacement p)
    {
        var wantAbove = IsAbove(_wanted ?? PlacementMode.Bottom);
        var above = wantAbove;
        if (Room(p.Target, p.AnchorRectangle) is { } room)
        {
            var need = p.PopupSize.Height + _gap;
            double roomWanted = wantAbove ? room.Above : room.Below, roomOther = wantAbove ? room.Below : room.Above;
            if (need > roomWanted)
            {
                above = need <= roomOther || roomOther > roomWanted ? !wantAbove : wantAbove;
                if (need > Math.Max(roomWanted, roomOther)) Cap(Math.Max(roomWanted, roomOther) - _gap - _room.Top - _room.Bottom);
            }
        }
        Widen(p.AnchorRectangle.Width);
        p.Anchor = (above ? PopupAnchor.Top : PopupAnchor.Bottom) | PopupAnchor.Left;
        p.Gravity = (above ? PopupGravity.Top : PopupGravity.Bottom) | PopupGravity.Right;
        p.Offset = new Point(-_room.Left, above ? _room.Bottom - _gap : _gap - _room.Top);
        p.ConstraintAdjustment = Adjustment;
    }

    // The pop-down's most height: set only when it changes, since this runs inside the pop-up's layout.
    private void Cap(double height)
    {
        if (Popup.Child is { } child && height > 0 && child.MaxHeight != height) child.MaxHeight = height;
    }

    // At least as wide as the opener (never narrower than its style's least width): raised only, for the same reason.
    private void Widen(double width)
    {
        if (Popup.Child is { } child && child.MinWidth < width) child.MinWidth = width;
    }

    // Room above and below the opener inside its screen's working area, in the opener's units; null when the screen is unknown.
    private static (double Above, double Below)? Room(Visual target, Rect anchorRect)
    {
        var top = TopLevel.GetTopLevel(target);
        var screen = top?.Screens?.ScreenFromVisual(target);
        if (top is null || screen is null || screen.Scaling <= 0) return null;
        var work = screen.WorkingArea;
        var upper = top.PointToScreen(anchorRect.TopLeft).Y;
        var lower = top.PointToScreen(anchorRect.BottomLeft).Y;
        return ((upper - work.Y) / screen.Scaling, (work.Bottom - lower) / screen.Scaling);
    }

    private static bool IsAboveOrBelow(PlacementMode mode) => mode is PlacementMode.Top or PlacementMode.Bottom
        or PlacementMode.TopEdgeAlignedLeft or PlacementMode.TopEdgeAlignedRight
        or PlacementMode.BottomEdgeAlignedLeft or PlacementMode.BottomEdgeAlignedRight;

    private static bool IsAbove(PlacementMode mode) => mode is PlacementMode.Top
        or PlacementMode.TopEdgeAlignedLeft or PlacementMode.TopEdgeAlignedRight;
}
