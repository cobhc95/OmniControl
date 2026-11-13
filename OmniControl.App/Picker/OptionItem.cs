using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Mouse;
using OmniControl.App.Pages;
using OmniControl.App.Services;

namespace OmniControl.App.Picker;

/// <summary>One setting row on an entry's settings page: a number, a choice, a switch or a text.</summary>
public sealed partial class OptionItem : ObservableObject
{
    public ActionOption Option { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display), nameof(IsOn))]
    private string _value;

    public string Label => Option.Label;

    /// <summary>Numbers and choices are stepped with Left/Right or the two small buttons.</summary>
    public bool IsStepper => Option.Kind is OptionKind.Number or OptionKind.Choice;
    public bool IsNumber => Option.Kind == OptionKind.Number;
    public bool IsToggle => Option.Kind == OptionKind.Toggle;
    public bool IsText => Option.Kind == OptionKind.Text;
    public string Hint => Option.Hint;

    /// <summary>True while a number is being typed in instead of stepped (Enter or a double-click on it).</summary>
    [ObservableProperty] private bool _isTyping;

    /// <summary>What is in the number box while <see cref="IsTyping"/>.</summary>
    [ObservableProperty] private string _typed = string.Empty;

    /// <summary>The value as shown beside the label ("24 px", "Google").</summary>
    public string Display => Option.Kind == OptionKind.Number ? $"{Value}{Option.Unit}" : Value;

    public bool IsOn
    {
        get => Value == "1";
        set => Value = value ? "1" : "0";
    }

    public OptionItem(ActionOption option, string value)
    {
        Option = option;
        _value = value;
    }

    /// <summary>Left/Right: a number goes down or up by its step, a choice goes to the previous or next entry (wrapping), a switch goes off or on.</summary>
    public void Step(int direction)
    {
        switch (Option.Kind)
        {
            case OptionKind.Number:
                var number = int.TryParse(Value, out var n) ? n : int.Parse(Option.Default);
                Value = Math.Clamp(number + direction * Option.Step, Option.Min, Option.Max).ToString();
                break;
            case OptionKind.Choice:
                var choices = Option.Choices!;
                Value = choices[Wrap.Index(Math.Max(0, choices.ToList().IndexOf(Value)) + direction, choices.Count)];
                break;
            case OptionKind.Toggle:
                IsOn = direction > 0;
                break;
        }
    }

    /// <summary>Opens the number for typing (only a number can be typed).</summary>
    public void BeginTyping()
    {
        if (Option.Kind != OptionKind.Number) return;
        Typed = Value;
        IsTyping = true;
    }

    /// <summary>Takes what was typed (<see cref="TypedNumber"/>: a whole number held within the setting's limits).</summary>
    public void CommitTyping()
    {
        if (!IsTyping) return;
        if (TypedNumber.TryRead(Typed, Option.Min, Option.Max, out var number)) Value = number.ToString();
        IsTyping = false;
    }

    public void CancelTyping() => IsTyping = false;

    /// <summary>Space: a switch flips.</summary>
    public void Flip()
    {
        if (IsToggle) IsOn = !IsOn;
    }
}
