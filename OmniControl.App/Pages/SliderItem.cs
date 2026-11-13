using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages;

/// <summary>A slider (brightness, contrast, EQ band...) that writes to hardware or settings when moved. Defaults to 0-100.</summary>
public sealed partial class SliderItem : ObservableObject, IEditorItem, IHalfWidth
{
    private readonly Func<int, OperationResult> _apply;
    private readonly Action<string> _report;
    private bool _ready;

    public string Label { get; }
    public int Minimum { get; }
    public int Maximum { get; }
    public string Unit { get; }

    /// <summary>Where a double-click on the slider puts it: the given default, else 0 for a signed range, else the top.</summary>
    public int ResetValue { get; }

    /// <summary>Greyed out, with a reason, while the control cannot be used (a monitor that does not report it).</summary>
    public Availability Availability { get; } = new();

    /// <summary>An on/off that belongs with this slider, shown at the end of the same line after a divider (Gestures: "Show margins").</summary>
    public ToggleItem? Companion { get; init; }

    /// <summary>True: the card takes half the width (label, slider and value on one line, see <see cref="HalfPanel"/>). A companion is not shown on a half card.</summary>
    public bool Half { get; init; }

    /// <summary>Which of the card's three layouts the view shows: full width, full width with the companion switch, or half width.</summary>
    public bool IsPlain => !Half && Companion is null;
    public bool HasCompanionLine => !Half && Companion is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    private int _value;

    public string ValueText => Minimum < 0 && Value > 0 ? $"+{Value}{Unit}" : $"{Value}{Unit}";

    public SliderItem(string label, int initial, Func<int, OperationResult> apply, Action<string> report,
        int minimum = 0, int maximum = 100, string unit = "", int? defaultValue = null)
    {
        Label = label;
        _apply = apply;
        _report = report;
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit;
        ResetValue = defaultValue ?? (minimum < 0 ? 0 : maximum);
        _value = initial;
        _ready = true;
    }

    /// <summary>Moves the slider to a value read from elsewhere without writing it back.</summary>
    public void Load(int value)
    {
        _ready = false;
        Value = value;
        _ready = true;
    }

    partial void OnValueChanged(int value)
    {
        if (!_ready) return;
        var r = _apply(value);
        if (!r.Success) _report(r.Message);
    }
}
