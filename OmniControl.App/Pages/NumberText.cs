using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OmniControl.App.Pages;

/// <summary>
/// A slider's value text that can be typed into (AGENTS.md design principle 3), the same way a number setting of an action is
/// (<see cref="Mouse.OptionItem"/>): a double-click on the text, or Ctrl+Enter on the slider beside it, turns it into a small box;
/// Enter, a click elsewhere or leaving the box applies the number (<see cref="TypedNumber"/>: held within
/// <see cref="Minimum"/>..<see cref="Maximum"/>), Esc cancels. The text is the child declared in XAML, so it keeps its own look.
/// </summary>
public sealed class NumberText : Panel
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<NumberText, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<NumberText, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<NumberText, double>(nameof(Maximum));

    /// <summary>How many decimals a typed value keeps (0 = whole numbers, the usual case; the equaliser's Hz, dB and Q use more).</summary>
    public static readonly StyledProperty<int> DecimalsProperty = AvaloniaProperty.Register<NumberText, int>(nameof(Decimals));

    /// <summary>The style class of the box (its size lives in Theme.axaml).</summary>
    private const string BoxClass = "typednumber";

    private readonly TextBox _box = new() { IsVisible = false };
    private TopLevel? _watching;

    public NumberText()
    {
        Background = Brushes.Transparent; // the whole value area takes the double-click, not just the glyphs
        _box.Classes.Add(BoxClass);
        Children.Add(_box);
        AddHandler(DoubleTappedEvent, OnDoubleTapped);
        _box.AddHandler(KeyDownEvent, OnBoxKeyDown, RoutingStrategies.Tunnel);
        _box.LostFocus += (_, _) => CommitTyping();
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Decimals { get => GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }

    private bool IsTyping => _box.IsVisible;

    /// <summary>True when <paramref name="focused"/> is a value box being typed in (so Esc and Enter belong to it).</summary>
    public static bool IsTypingIn(object? focused) => focused is TextBox box && box.Classes.Contains(BoxClass) && box.IsVisible;

    /// <summary>Ctrl+Enter on a slider: opens the value text beside it for typing. False when the slider has none.</summary>
    public static bool TryBeginBeside(Control part)
    {
        if (part.GetVisualParent()?.GetVisualDescendants().OfType<NumberText>().FirstOrDefault(n => n.IsEffectivelyVisible) is not { } text) return false;
        text.BeginTyping();
        return true;
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (IsTyping) return;
        BeginTyping();
        e.Handled = true;
    }

    private void BeginTyping()
    {
        _box.Text = TypedNumber.Show(Value, Decimals);
        ShowBox(true);
        _watching = TopLevel.GetTopLevel(this);
        _watching?.AddHandler(PointerPressedEvent, OnAnyPress, RoutingStrategies.Tunnel, handledEventsToo: true);
        // Focus once the box has its template.
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsTyping) return;
            _box.Focus(NavigationMethod.Directional);
            _box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void CommitTyping()
    {
        if (!IsTyping) return;
        if (TypedNumber.TryRead(_box.Text, Minimum, Maximum, Decimals, out var number)) Value = number;
        EndTyping();
    }

    private void CancelTyping()
    {
        if (IsTyping) EndTyping();
    }

    private void EndTyping()
    {
        _watching?.RemoveHandler(PointerPressedEvent, OnAnyPress);
        _watching = null;
        var hadFocus = _box.IsFocused;
        ShowBox(false);
        // Back on the card's control, so the keyboard carries on from where it was.
        if (hadFocus && this.FindAncestorOfType<ListBoxItem>() is { } card) RowEditing.CardTarget(card).Focus(NavigationMethod.Directional);
    }

    private void ShowBox(bool typing)
    {
        foreach (var child in Children) child.IsVisible = (child == _box) == typing;
    }

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: CommitTyping(); break;
            case Key.Escape: CancelTyping(); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>A click anywhere but in the box applies the typed number, like Enter.</summary>
    private void OnAnyPress(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual source && (source == _box || _box.IsVisualAncestorOf(source))) return;
        CommitTyping();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelTyping(); // releases the window-wide click handler
        base.OnDetachedFromVisualTree(e);
    }
}
