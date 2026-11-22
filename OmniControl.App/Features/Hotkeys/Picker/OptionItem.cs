using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// One setting row on an entry's settings page: a number, a choice, a switch or a text.
public sealed partial class OptionItem : ObservableObject
{
    public ActionOption Option { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display), nameof(IsOn), nameof(Tag), nameof(HasTag), nameof(HasNote))]
    private string _value;

    public string Label => Option.Label;

    /// Numbers and choices are stepped with Left/Right (and the wheel); a number shows the two small buttons, a choice the shared pull-down.
    public bool IsStepper => Option.Kind is OptionKind.Number or OptionKind.Choice;
    public bool IsNumber => Option.Kind == OptionKind.Number;
    public bool IsChoice => Option.Kind == OptionKind.Choice;
    public IReadOnlyList<string>? Choices => Option.Choices;
    public bool IsToggle => Option.Kind == OptionKind.Toggle;

    /// A number, choice or switch sits on the label line; a text or path gets the pane width on a line of its own below the label.
    public bool IsInline => IsStepper || IsToggle;
    public bool IsBlock => !IsInline;
    public bool IsText => Option.Kind == OptionKind.Text && Option.Browse is null;

    /// A text that is a path: shown with the shared path field and its Browse button.
    public bool IsPath => Option.Kind == OptionKind.Text && Option.Browse is not null;
    public PathKind PathKind => Option.Browse ?? PathKind.File;

    /// A point on the screen: shown with the shared point field and its Pick button.
    public bool IsPoint => Option.Kind == OptionKind.Point;

    /// A group's heading (no value), with its note under it when it has one.
    public bool IsHeading => Option.Kind == OptionKind.Heading;
    public bool IsLabel => !IsHeading;
    public bool HasHeadingNote => IsHeading && Option.Hint.Length > 0;
    /// A whole editor as one row (<see cref="ActionOption.Panel"/>): its view-model, made when the settings pane shows the row; null for any other row.
    public ISettingsPanel? PanelModel { get; }
    public bool IsPanel => Option.Kind == OptionKind.Panel;
    public string PickerTitle => $"Choose: {Option.Label}";
    public string Hint => Option.Hint;

    /// A muted line under a switch's label, or under any row's while its value is one its option names (<see cref="ActionOption.NotedWhen"/>).
    public bool HasNote => Option.Hint.Length > 0 && (Option.NoteValues?.Contains(Value) ?? IsToggle);

    /// A caution tag shown on the row while its option says so (<see cref="ActionOption.TaggedWhen"/>), or what is wrong with its text (<see cref="ActionOption.CheckedBy"/>).
    public string? Tag => Option.Check?.Invoke(Value) ?? Option.TagNow?.Invoke();
    public bool HasTag => Tag is { Length: > 0 };

    /// False while the setting this row needs is off: the row is dimmed and its tooltip says why.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Opacity), nameof(Tip))]
    private bool _available = true;

    public double Opacity => Available ? 1 : 0.45;
    public string? Tip => Available ? null : Option.NeedsReason;

    /// True while a number is being typed in instead of stepped (Enter or a double-click on it).
    [ObservableProperty] private bool _isTyping;

    /// What is in the number box while <see cref="IsTyping"/>.
    [ObservableProperty] private string _typed = string.Empty;

    /// The value as shown beside the label ("24 px", "Google").
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
        PanelModel = option.MakePanel?.Invoke();
    }

    /// Left/Right: a number goes down or up by its step, a choice goes to the previous or next entry (wrapping), a switch goes off or on.
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

    /// Opens the number for typing (only a number can be typed).
    public void BeginTyping()
    {
        if (Option.Kind != OptionKind.Number) return;
        Typed = Value;
        IsTyping = true;
    }

    /// Takes what was typed (<see cref="TypedNumber"/>: a whole number held within the setting's limits).
    public void CommitTyping()
    {
        if (!IsTyping) return;
        if (TypedNumber.TryRead(Typed, Option.Min, Option.Max, out var number)) Value = number.ToString();
        IsTyping = false;
    }

    public void CancelTyping()
    {
        IsTyping = false;
        PanelModel?.CancelTyping();
    }

    /// Space: a switch flips.
    public void Flip()
    {
        if (IsToggle) IsOn = !IsOn;
    }
}
