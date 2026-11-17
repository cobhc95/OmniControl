using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.Core.Engine;

namespace OmniControl.App.Pages.Devices;

/// <summary>One text box in a step's editor: a field of the step (its words come from <see cref="SimSteps.LabelOf"/>); a change goes to the step at once.</summary>
public sealed partial class StepFieldItem : ObservableObject
{
    private readonly SimStep _step;
    private readonly Action _edited;

    public StepFieldItem(SimStep step, SimField field, Action edited)
    {
        _step = step;
        Field = field;
        _edited = edited;
        _value = SimSteps.Get(step, field);
    }

    public SimField Field { get; }
    public string Label => SimSteps.LabelOf(Field);

    [ObservableProperty] private string _value;

    partial void OnValueChanged(string value)
    {
        SimSteps.Set(_step, Field, value);
        _edited();
    }
}

/// <summary>
/// One step of a simulated device's recording as a row of the sentence list: its number, its words with the block that opens its editor,
/// the reason it failed (outlined red) or is incomplete, and its Re-pick, Test step and remove. What the step does is <see cref="SimSteps"/>'s.
/// </summary>
public sealed partial class SimStepItem : ObservableObject
{
    private readonly Action<SimStepItem> _edited, _remove;
    private readonly Func<SimStepItem, Task> _test, _rePick;

    public SimStepItem(SimStep step, Action<SimStepItem> edited, Action<SimStepItem> remove, Func<SimStepItem, Task> test, Func<SimStepItem, Task> rePick)
    {
        Step = step;
        _edited = edited;
        _remove = remove;
        _test = test;
        _rePick = rePick;
        Fields = SimSteps.FieldsOf(step.Kind).Select(f => new StepFieldItem(step, f, Changed)).ToList();
    }

    public SimStep Step { get; }
    public IReadOnlyList<StepFieldItem> Fields { get; }
    public bool HasFields => Fields.Count > 0;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(NumberText), nameof(Note), nameof(HasNote), nameof(IsIncomplete), nameof(ShowsCaution))] private int _number;

    /// <summary>Why the last run stopped here.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Failure), nameof(IsFailed), nameof(Note), nameof(HasNote))] private string _runFailure = string.Empty;

    /// <summary>Why the last Test step of this step failed.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Failure), nameof(IsFailed), nameof(Note), nameof(HasNote))] private string _testFailure = string.Empty;

    /// <summary>The next touch in the emulator will replace this step's element.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(RePickText))] private bool _isPicking;

    public string NumberText => Number.ToString();
    public string Failure => RunFailure.Length > 0 ? RunFailure : TestFailure;
    public bool IsFailed => Failure.Length > 0;

    /// <summary>The step cannot run yet (nothing picked, no app, ...).</summary>
    public bool IsIncomplete => SimSteps.Missing(Step) is not null;

    /// <summary>The note is a failure or something missing: shown in caution words.</summary>
    public bool ShowsCaution => IsFailed || IsIncomplete;

    /// <summary>The words under the row: the failure, else one sentence on what the step needs, else nothing.</summary>
    public string Note => IsFailed ? Failure : SimSteps.Fix(Step, Number) ?? string.Empty;
    public bool HasNote => Note.Length > 0;

    public string Lead => SimSteps.Words(Step).Lead;
    public string Target => SimSteps.Words(Step).Target;
    public string Tail => SimSteps.Words(Step).Tail;
    public string TargetTip => SimSteps.Detail(Step);
    public bool HasTail => Tail.Length > 0;

    /// <summary>Only steps that point at something on the screen can be picked again.</summary>
    public bool CanRePick => Step.Kind is SimStepKind.Tap or SimStepKind.WaitFor or SimStepKind.Check;

    public string RePickText => IsPicking ? "Tap it in the emulator..." : "Re-pick";

    public string RePickTip => Step.Kind == SimStepKind.Check
        ? "Then tap, in the emulator, the words the screen must show."
        : "Then tap the element in the emulator; it replaces this step's element.";

    /// <summary>Anything about the step changed: its words and note are read again and the owner saves.</summary>
    private void Changed()
    {
        RunFailure = string.Empty;
        TestFailure = string.Empty;
        Notify();
        _edited(this);
    }

    /// <summary>Reads the words and the note again (the step was changed from outside).</summary>
    public void Notify() => OnPropertyChanged(string.Empty);

    [RelayCommand]
    private void Remove() => _remove(this);

    [RelayCommand]
    private Task Test() => _test(this);

    [RelayCommand]
    private Task RePick() => _rePick(this);

    /// <summary>The element was picked again: the fields show what the step holds now.</summary>
    public void Picked()
    {
        foreach (var field in Fields) field.Value = SimSteps.Get(Step, field.Field);
        Changed();
    }
}
