namespace OmniControl.App.Shared.Ui.Pages;

/// THE rule that a tool tip with nothing in it never opens. Many tips are bound to text that is sometimes empty (a note, a reason, a
/// summary); when the tip becomes null, empty or only spaces (or a <c>TextBlock</c> with no text) it is taken away until a real value
/// is bound again. <see cref="Register"/> is called once at start-up; no control or view-model guards its own tip.
internal static class EmptyToolTips
{
    public static void Register() => ToolTip.TipProperty.Changed.AddClassHandler<Control>(OnTipChanged);

    private static void OnTipChanged(Control control, AvaloniaPropertyChangedEventArgs e)
    {
        if (IsEmpty(e.NewValue) && e.NewValue is not null) control.SetCurrentValue(ToolTip.TipProperty, null);
    }

    internal static bool IsEmpty(object? tip) => tip switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        TextBlock t => string.IsNullOrWhiteSpace(t.Text) && t.Inlines is not { Count: > 0 },
        _ => false,
    };
}
