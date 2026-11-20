using Avalonia.Data;
using Avalonia.Input;

namespace OmniControl.App.Shared.Ui.Controls;

/// THE "Edit" menu of a window's menu bar for its <see cref="UndoHistory"/>: "Undo &lt;step&gt;" and "Redo &lt;step&gt;" (greyed when there
/// is none), and Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z anywhere in its window while it is shown. A focused text box keeps its own Ctrl+Z.
/// A window opts in with one line in its <c>Menu</c>: <c>&lt;c:EditMenu History="{Binding Undo}" /&gt;</c>.
public sealed class EditMenu : MenuItem
{
    public static readonly StyledProperty<UndoHistory?> HistoryProperty = AvaloniaProperty.Register<EditMenu, UndoHistory?>(nameof(History));

    private TopLevel? _top;

    public UndoHistory? History
    {
        get => GetValue(HistoryProperty);
        set => SetValue(HistoryProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(MenuItem);

    public EditMenu()
    {
        Header = "_Edit";
        Items.Add(Item(nameof(UndoHistory.UndoText), nameof(UndoHistory.UndoCommand), "Ctrl+Z"));
        Items.Add(Item(nameof(UndoHistory.RedoText), nameof(UndoHistory.RedoCommand), "Ctrl+Y"));
    }

    private MenuItem Item(string text, string command, string gesture) => new()
    {
        [!HeaderProperty] = new Binding($"{nameof(History)}.{text}") { Source = this },
        [!CommandProperty] = new Binding($"{nameof(History)}.{command}") { Source = this },
        InputGesture = KeyGesture.Parse(gesture),
    };

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _top = TopLevel.GetTopLevel(this);
        _top?.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _top?.RemoveHandler(KeyDownEvent, OnKeyDown);
        _top = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || History is not { } history || _top?.FocusManager?.GetFocusedElement() is TextBox) return;
        e.Handled = history.HandleKey(e.Key, e.KeyModifiers);
    }
}
