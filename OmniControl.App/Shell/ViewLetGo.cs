using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace OmniControl.App.Shell;

/// What a view needs before it is let go (a whole window after a long hide, or one page's view): an open tooltip or pop-up is a window of
/// its own holding the control it belongs to, and the keyboard keeps its focused control; either would keep the view in memory.
internal static class ViewLetGo
{
    public static void Clear(Visual root)
    {
        foreach (var control in root.GetVisualDescendants().OfType<Control>())
        {
            if (ToolTip.GetIsOpen(control)) ToolTip.SetIsOpen(control, false);
            if (control is Popup { IsOpen: true } popup) popup.IsOpen = false;
            if (control is Button { Flyout: { IsOpen: true } flyout }) flyout.Hide();
            if (FlyoutBase.GetAttachedFlyout(control) is { IsOpen: true } attached) attached.Hide();
        }
        if (TopLevel.GetTopLevel(root)?.FocusManager is { } focus && focus.GetFocusedElement() is Visual focused && root.IsVisualAncestorOf(focused)) focus.ClearFocus();
    }
}
