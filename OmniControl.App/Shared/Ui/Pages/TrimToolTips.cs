using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Shared.Ui.Pages;

/// THE rule that a tool tip never repeats words already on screen: a <c>TextBlock</c> with trimming that has no tip of its own gets its full
/// text as a tip, only while the words are really cut off (checked when the pointer arrives, so resizing and live text are always right).
/// A text under a control that has its own tip keeps that tip. <see cref="Register"/> is called once at start-up; no control or view-model
/// binds its own name as a tip.
internal static class TrimToolTips
{
    private static readonly AttachedProperty<bool> OwnTipProperty = AvaloniaProperty.RegisterAttached<TextBlock, TextBlock, bool>("OwnTip");

    public static void Register() => InputElement.PointerEnteredEvent.AddClassHandler<TextBlock>(OnEnter);

    private static void OnEnter(TextBlock text, PointerEventArgs e)
    {
        var own = text.GetValue(OwnTipProperty);
        if (!own && ToolTip.GetTip(text) is not null) return;
        var cut = text.TextTrimming != TextTrimming.None && !string.IsNullOrEmpty(text.Text) && IsCut(text) && !TipAbove(text);
        if (cut)
        {
            text.SetValue(OwnTipProperty, true);
            ToolTip.SetTip(text, text.Text);
        }
        else if (own)
        {
            text.SetValue(OwnTipProperty, false);
            text.ClearValue(ToolTip.TipProperty);
        }
    }

    private static bool IsCut(TextBlock text) => text.TextLayout.TextLines.Any(line => line.HasOverflowed);

    private static bool TipAbove(Visual from)
    {
        for (var parent = from.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            if (parent is Control c && ToolTip.GetTip(c) is not null) return true;
        }
        return false;
    }
}
