using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace OmniControl.App.Controls;

/// <summary>
/// The master knob (AGENTS.md "Master design elements"): a round value control with a 270 degree sweep and its value text under it,
/// drawn by its template (Styles/Knobs.axaml: a <see cref="KnobFace"/> and the value text). <see cref="FromCentre"/> = a pan knob, whose
/// arc runs from the centre mark. Mouse: drag up or down turns it; one click without a drag asks for the value to be typed
/// (<see cref="TypeRequestedEvent"/>, after the double-click time, so the two never mix: the owner's rule for knobs); a double-click
/// resets it (Pages/SliderReset, as on every slider); the wheel steps it (Pages/ValueWheel). Keys: <see cref="KnobMath.KeyStep"/>; in a
/// card list the shared row keys (Pages/RowEditing) decide first, as for a slider. The maths is <see cref="KnobMath"/>; this class only
/// turns pointer and key input into values. It knows nothing of the pages: typing and resetting are registered once by them.
/// </summary>
public class Knob : RangeBase
{
    public static readonly StyledProperty<bool> FromCentreProperty = AvaloniaProperty.Register<Knob, bool>(nameof(FromCentre));

    /// <summary>The value as words under the knob ("C", "L 20", "100"); the view-model says how a value reads.</summary>
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<Knob, string?>(nameof(Text));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty = KnobFace.TrackBrushProperty.AddOwner<Knob>();
    public static readonly StyledProperty<IBrush?> ValueBrushProperty = KnobFace.ValueBrushProperty.AddOwner<Knob>();
    public static readonly StyledProperty<IBrush?> PointerBrushProperty = KnobFace.PointerBrushProperty.AddOwner<Knob>();
    public static readonly StyledProperty<IBrush?> RingBrushProperty = KnobFace.RingBrushProperty.AddOwner<Knob>();

    /// <summary>A click (no drag, no double-click) asks for the value to be typed in; Pages/NumberText answers it.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> TypeRequestedEvent =
        RoutedEvent.Register<Knob, RoutedEventArgs>("TypeRequested", RoutingStrategies.Bubble);

    private Point? _pressedAt;
    private double _startValue;
    private bool _moved;
    private int _clickCount;
    private DispatcherTimer? _typeTimer;

    static Knob() => FocusableProperty.OverrideDefaultValue<Knob>(true);

    public bool FromCentre { get => GetValue(FromCentreProperty); set => SetValue(FromCentreProperty, value); }
    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? ValueBrush { get => GetValue(ValueBrushProperty); set => SetValue(ValueBrushProperty, value); }
    public IBrush? PointerBrush { get => GetValue(PointerBrushProperty); set => SetValue(PointerBrushProperty, value); }
    public IBrush? RingBrush { get => GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }

    /// <summary>Asks for the value to be typed in now (a click, or Ctrl+Enter from the keyboard).</summary>
    public void RequestTyping() => RaiseEvent(new RoutedEventArgs(TypeRequestedEvent, this));

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        StopTypeTimer(); // a second press of a double-click: the reset follows, not the typing
        _pressedAt = e.GetPosition(this);
        _startValue = Value;
        _moved = false;
        _clickCount = e.ClickCount;
        e.Pointer.Capture(this);
        Focus(NavigationMethod.Pointer);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_pressedAt is not { } start) return;
        var at = e.GetPosition(this);
        if (!_moved && !KnobMath.IsDrag(at.X - start.X, at.Y - start.Y)) return;
        _moved = true;
        Value = KnobMath.DragValue(_startValue, start.Y - at.Y, Minimum, Maximum);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_pressedAt is null) return;
        _pressedAt = null;
        e.Pointer.Capture(null);
        if (KnobMath.GestureOf(_moved, _clickCount) == KnobGesture.Click) StartTypeTimer();
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _pressedAt = null;
    }

    /// <summary>The typing waits for the double-click time, so the first click of a double-click never opens the box.</summary>
    private void StartTypeTimer()
    {
        var wait = TopLevel.GetTopLevel(this)?.PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.Zero;
        _typeTimer = new DispatcherTimer { Interval = wait };
        _typeTimer.Tick += OnTypeTimer;
        _typeTimer.Start();
    }

    private void OnTypeTimer(object? sender, EventArgs e)
    {
        StopTypeTimer();
        RequestTyping();
    }

    private void StopTypeTimer()
    {
        if (_typeTimer is null) return;
        _typeTimer.Stop();
        _typeTimer.Tick -= OnTypeTimer;
        _typeTimer = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
        {
            RequestTyping();
            e.Handled = true;
            return;
        }
        if (KnobMath.KeyStep(e.Key, e.KeyModifiers, Minimum, Maximum) is not { } by) return;
        Value = KnobMath.Stepped(Value, by, Minimum, Maximum);
        e.Handled = true;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopTypeTimer();
        base.OnDetachedFromVisualTree(e);
    }
}
