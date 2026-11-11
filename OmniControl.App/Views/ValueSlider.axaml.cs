using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace OmniControl.App.Views;

public partial class ValueSlider : UserControl
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ValueSlider, double>(nameof(Value), 0, defaultBindingMode: BindingMode.TwoWay, coerce: CoerceValue);
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(Minimum), 0);
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> DefaultValueProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(DefaultValue), 0);
    public static readonly StyledProperty<string?> LabelProperty = AvaloniaProperty.Register<ValueSlider, string?>(nameof(Label));
    public static readonly StyledProperty<double> StepProperty = AvaloniaProperty.Register<ValueSlider, double>(nameof(Step), 1);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double DefaultValue { get => GetValue(DefaultValueProperty); set => SetValue(DefaultValueProperty, value); }
    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public double Step { get => GetValue(StepProperty); set => SetValue(StepProperty, value); }

    private bool _dragging;
    private readonly Grid _track;
    private readonly Border _fill, _thumb, _frame;
    private readonly TextBox _box;
    private readonly TextBlock _label;

    public ValueSlider()
    {
        InitializeComponent();
        Focusable = true;
        _track = this.FindControl<Grid>("TrackHost")!;
        _fill = this.FindControl<Border>("Fill")!;
        _thumb = this.FindControl<Border>("Thumb")!;
        _frame = this.FindControl<Border>("FocusFrame")!;
        _box = this.FindControl<TextBox>("ValueBox")!;
        _label = this.FindControl<TextBlock>("LabelText")!;

        _track.PointerPressed += OnTrackPressed;
        _track.PointerMoved += OnTrackMoved;
        _track.PointerReleased += (_, e) => { _dragging = false; e.Pointer.Capture(null); };
        _track.SizeChanged += (_, _) => UpdateVisual();
        _box.KeyDown += OnBoxKeyDown;
        _box.LostFocus += (_, _) => CommitText();
        DoubleTapped += OnDoubleTapped;

        UpdateVisual();
    }

    private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);

    private static double CoerceValue(AvaloniaObject o, double v)
    {
        var s = (ValueSlider)o;
        var min = s.Minimum;
        var max = Math.Max(s.Maximum, min);
        return Math.Clamp(double.IsNaN(v) ? min : v, min, max);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MinimumProperty || change.Property == MaximumProperty) CoerceValue(ValueProperty);
        if (change.Property == ValueProperty || change.Property == MinimumProperty
            || change.Property == MaximumProperty || change.Property == LabelProperty)
            UpdateVisual();
    }

    private string Fmt() => ((int)Math.Round(Value)).ToString(CultureInfo.InvariantCulture);

    private void UpdateVisual()
    {
        if (_track == null) return;
        _label.Text = Label;
        _label.IsVisible = !string.IsNullOrEmpty(Label);
        if (!_box.IsFocused) _box.Text = Fmt();
        var range = Maximum - Minimum;
        var pct = range <= 0 ? 0 : (Value - Minimum) / range;
        var tx = Math.Max(0, _track.Bounds.Width - 16) * pct;
        _thumb.Margin = new Thickness(tx, 0, 0, 0);
        _fill.Width = tx + 8;
    }

    private void SetFromPointer(PointerEventArgs e)
    {
        var w = _track.Bounds.Width - 16;
        if (w <= 0) return;
        var pct = Math.Clamp((e.GetPosition(_track).X - 8) / w, 0, 1);
        var v = Minimum + pct * (Maximum - Minimum);
        if (Step > 0) v = Minimum + Math.Round((v - Minimum) / Step) * Step;
        Value = v;
    }

    private void OnTrackPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_track).Properties.IsLeftButtonPressed) return;
        Focus();
        _dragging = true;
        e.Pointer.Capture(_track);
        SetFromPointer(e);
    }

    private void OnTrackMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging) SetFromPointer(e);
    }

    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        Value = DefaultValue;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Source is TextBox) return;
        double? v = e.Key switch
        {
            Key.Left or Key.Down => Value - Step,
            Key.Right or Key.Up => Value + Step,
            Key.PageUp => Value + 10,
            Key.PageDown => Value - 10,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => null
        };
        if (v is { } nv) { Value = nv; e.Handled = true; }
    }

    private void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitText(); e.Handled = true; Focus(); }
        else if (e.Key == Key.Escape) { _box.Text = Fmt(); e.Handled = true; Focus(); }
    }

    private void CommitText()
    {
        if (double.TryParse(_box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            Value = v;
        _box.Text = Fmt();
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        if (e.Source == this && this.TryFindResource("BrushAccent", ActualThemeVariant, out var b) && b is IBrush br)
            _frame.BorderBrush = br;
    }

    protected override void OnLostFocus(RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        if (!IsKeyboardFocusWithin) _frame.BorderBrush = Brushes.Transparent;
    }
}
