using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OmniControl.App.Controls;

namespace OmniControl.App.Pages;

/// <summary>
/// A slider's value text that can be typed into (AGENTS.md design principle 3), the same way a number setting of an action is
/// (<see cref="Mouse.OptionItem"/>): a double-click on the text, or Ctrl+Enter on the slider beside it, turns it into a small box;
/// Enter, a click elsewhere or leaving the box applies the number (<see cref="TypedNumber"/>: held within
/// <see cref="Minimum"/>..<see cref="Maximum"/>), Esc cancels. The text is the child declared in XAML, so it keeps its own look.
/// A <see cref="Knob"/>'s value text is one too: a click or double-click on the box opens it and never resets the knob, and the knob
/// face's own click or right-click asks for it (the owner's rule: there a double-click resets): <see cref="Register"/> answers every
/// knob's request once, at start-up.
/// </summary>
public sealed class NumberText : Panel
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<NumberText, double>(nameof(Value), defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<NumberText, double>(nameof(Minimum));
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<NumberText, double>(nameof(Maximum));

    /// <summary>How many decimals a typed value keeps (0 = whole numbers, the usual case; the equaliser's Hz, dB and Q use more).</summary>
    public static readonly StyledProperty<int> DecimalsProperty = AvaloniaProperty.Register<NumberText, int>(nameof(Decimals));

    /// <summary>Every knob's click (or Ctrl+Enter) opens the value text in its template for typing. Called once at start-up.</summary>
    public static void Register() => Knob.TypeRequestedEvent.AddClassHandler<Knob>((knob, e) =>
    {
        if (knob.GetVisualDescendants().OfType<NumberText>().FirstOrDefault() is not { } text) return;
        text.BeginTyping();
        e.Handled = true;
    });

    /// <summary>The style class of the box (its size lives in Theme.axaml).</summary>
    private const string BoxClass = "typednumber";

    /// <summary>The field look's resources (App.axaml, Brushes.axaml): the inset between the frame and the value, and its corner radius.</summary>
    private const string InsetKey = "TypeableInset", CornerKey = "TypeableCornerRadius";

    private readonly TextBox _box = new() { IsVisible = false };
    private readonly Border _frame = new() { BorderThickness = new Thickness(1), IsHitTestVisible = false };
    private TopLevel? _watching;

    public NumberText()
    {
        Background = Brushes.Transparent; // the whole value area takes the double-click, not just the glyphs
        Cursor = new Cursor(StandardCursorType.Ibeam);
        _box.Classes.Add(BoxClass);
        Children.Add(_frame);
        Children.Add(_box);
        AddHandler(DoubleTappedEvent, OnDoubleTapped);
        AddHandler(PointerPressedEvent, OnPressed);
        _box.AddHandler(KeyDownEvent, OnBoxKeyDown, RoutingStrategies.Tunnel);
        _box.LostFocus += (_, _) => CommitTyping();
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Decimals { get => GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }

    private bool IsTyping => _box.IsVisible;

    // ---- the typeable look: a faint field box around the value (same thickness hovered or not, so nothing shifts) ----

    private Thickness Inset => this.TryFindResource(InsetKey, ActualThemeVariant, out var v) && v is Thickness t ? t : default;

    protected override Size MeasureOverride(Size availableSize)
    {
        var inset = Inset;
        var inner = new Size();
        foreach (var child in Children)
        {
            if (child == _frame) continue;
            child.Measure(availableSize.Deflate(inset));
            inner = new Size(Math.Max(inner.Width, child.DesiredSize.Width), Math.Max(inner.Height, child.DesiredSize.Height));
        }
        return inner.Inflate(inset);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var inset = Inset;
        _frame.Arrange(new Rect(finalSize));
        var inside = new Rect(inset.Left, inset.Top, Math.Max(0, finalSize.Width - inset.Left - inset.Right), Math.Max(0, finalSize.Height - inset.Top - inset.Bottom));
        foreach (var child in Children) if (child != _frame) child.Arrange(inside);
        return finalSize;
    }

    private void ShowFrame()
    {
        _frame.BorderBrush = (IBrush?)(this.TryFindResource(IsPointerOver ? "BrushAccent" : "BrushFieldEdge", ActualThemeVariant, out var edge) ? edge : null);
        _frame.Background = (IBrush?)(this.TryFindResource("BrushField", ActualThemeVariant, out var fill) ? fill : null);
        _frame.CornerRadius = this.TryFindResource(CornerKey, ActualThemeVariant, out var r) && r is CornerRadius c ? c : default;
        _frame.IsVisible = !IsTyping; // the text box has its own frame while typing
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        ShowFrame();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ShowFrame();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ShowFrame();
    }

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

    /// <summary>Inside a knob the value box opens on the first press and the knob never sees it (no drag, no reset from the box).</summary>
    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (this.FindAncestorOfType<Knob>() is null) return;
        if (!IsTyping) BeginTyping();
        e.Handled = true;
    }

    /// <summary>True when a gesture started on a value box (or in its text box): a reset or a drag must not take it.</summary>
    public static bool IsValueBox(object? source) =>
        source is Visual visual && (visual is NumberText || visual.FindAncestorOfType<NumberText>() is not null);

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
        // Back on the knob it belongs to, else on the card's control, so the keyboard carries on from where it was.
        if (!hadFocus) return;
        if (this.FindAncestorOfType<Knob>() is { } knob) knob.Focus(NavigationMethod.Directional);
        else if (this.FindAncestorOfType<ListBoxItem>() is { } card) RowEditing.CardTarget(card).Focus(NavigationMethod.Directional);
    }

    private void ShowBox(bool typing)
    {
        foreach (var child in Children) if (child != _frame) child.IsVisible = (child == _box) == typing;
        ShowFrame();
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
