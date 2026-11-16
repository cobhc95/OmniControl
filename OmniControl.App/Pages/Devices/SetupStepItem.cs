using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.Pages.Devices;

/// <summary>One line of a device's set-up checklist: what it is, how it stands (done, the one to do next, or later) and its one pill, if any.</summary>
public sealed partial class SetupStepItem : ObservableObject
{
    private readonly Func<bool> _done;
    private readonly Func<string> _words;

    /// <summary>The pill's words, or empty for a step with no pill.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasAction), nameof(HasGuide))] private string _actionLabel = string.Empty;

    [ObservableProperty] private bool _actionEnabled = true;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(TagText), nameof(IsDone), nameof(IsNext), nameof(IsLater))] private StepState _state;

    public string ActionTip { get; init; } = string.Empty;

    /// <summary>The pill runs this (null: the pill opens the router guide pop-down instead).</summary>
    public ICommand? ActionCommand { get; init; }

    /// <summary>The pill opens a pop-down of its own (the router guide), not a command.</summary>
    public bool OpensGuide { get; init; }

    public bool HasAction => ActionLabel.Length > 0 && !OpensGuide;
    public bool HasGuide => ActionLabel.Length > 0 && OpensGuide;

    public bool IsDone => State == StepState.Done;
    public bool IsNext => State == StepState.Next;
    public bool IsLater => State == StepState.Later;
    public string TagText => State switch { StepState.Done => "done", StepState.Next => "next", _ => "later" };

    public SetupStepItem(Func<bool> done, Func<string> words)
    {
        _done = done;
        _words = words;
    }

    /// <summary>Whether the step is done (asked of the device now) and its words, given whether an earlier step is still open.</summary>
    public void Refresh(bool earlierOpen)
    {
        Text = _words();
        State = _done() ? StepState.Done : earlierOpen ? StepState.Later : StepState.Next;
    }

    public bool Done => _done();
}

public enum StepState { Done, Next, Later }
