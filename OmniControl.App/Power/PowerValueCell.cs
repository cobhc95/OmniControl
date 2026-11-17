using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Engine;

namespace OmniControl.App.Power;

/// <summary>One ‹ value › of a power setting (plugged in or on battery): shows it, steps it, takes a typed number, and writes the change through its owner.</summary>
public sealed partial class PowerValueCell : ObservableObject
{
    private readonly PowerSetting _setting;
    private readonly Func<uint, bool> _write;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(Text))] private uint _value;
    [ObservableProperty] private bool _isTyping;
    [ObservableProperty] private string _typed = string.Empty;

    /// <param name="write">Writes the new value to Windows; true when Windows took it (the cell then shows it).</param>
    /// <param name="visible">False for the On battery cell of a PC without a battery.</param>
    public PowerValueCell(PowerSetting setting, uint value, bool visible, Func<uint, bool> write)
    {
        (_setting, _value, Visible, _write) = (setting, value, visible, write);
    }

    public bool Visible { get; }
    public string Text => PowerValueFormat.Show(_setting, Value);

    /// <summary>A number can be typed; a choice is stepped through Windows' list.</summary>
    public bool CanType => !_setting.IsChoice;

    [RelayCommand] private void StepDown() => Step(-1);
    [RelayCommand] private void StepUp() => Step(1);

    public void Step(int direction) => Set(PowerValueFormat.Next(_setting, Value, direction));

    public void BeginTyping()
    {
        if (!CanType) return;
        Typed = PowerValueFormat.ForTyping(_setting, Value);
        IsTyping = true;
    }

    /// <summary>Takes what was typed (a text that is not a number of this setting changes nothing).</summary>
    public void Commit()
    {
        if (!IsTyping) return;
        IsTyping = false;
        if (PowerValueFormat.TryParse(_setting, Typed, out var value)) Set(value);
    }

    public void CancelTyping() => IsTyping = false;

    private void Set(uint value)
    {
        if (value != Value && _write(value)) Value = value;
    }
}
