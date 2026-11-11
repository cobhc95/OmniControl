using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages;

/// <summary>A slider (brightness, contrast, EQ band...) that writes to hardware or settings when moved. Defaults to 0-100.</summary>
public sealed partial class SliderItem : ObservableObject, IEditorItem
{
    private readonly Func<int, OperationResult> _apply;
    private readonly Action<string> _report;
    private bool _ready;

    public string Label { get; }
    public int Minimum { get; }
    public int Maximum { get; }
    public string Unit { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValueText))]
    private int _value;

    public string ValueText => Minimum < 0 && Value > 0 ? $"+{Value}{Unit}" : $"{Value}{Unit}";

    public SliderItem(string label, int initial, Func<int, OperationResult> apply, Action<string> report,
        int minimum = 0, int maximum = 100, string unit = "")
    {
        Label = label;
        _apply = apply;
        _report = report;
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit;
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
