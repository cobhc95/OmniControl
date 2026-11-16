using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Pages.Volume.Easy.Flow;

/// <summary>One line of a checklist on screen. It is updated in place when the facts change, so a line ticks without the list being redrawn.</summary>
public sealed partial class EasyCheckItem : ObservableObject
{
    private readonly Func<string, Task> _run;

    public EasyCheckItem(EasyCheck check, Func<string, Task> run)
    {
        Id = check.Id;
        _run = run;
        FixCommand = new AsyncRelayCommand(() => _run(Id), () => HasFix && !IsBusy);
        Update(check);
    }

    public string Id { get; }

    public IAsyncRelayCommand FixCommand { get; }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _subtitle = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsDone), nameof(IsTodo), nameof(IsBlocked), nameof(Mark), nameof(HasFix))] private CheckState _state;
    [ObservableProperty] private string _fixLabel = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasFix))] private bool _hasFixButton;
    [ObservableProperty] private bool _isOptional;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasLive))] private string _live = string.Empty;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Mark), nameof(IsMarked))] private bool _isNote;

    /// <summary>A step is running (an installer is open): no fix button can be pressed meanwhile.</summary>
    [ObservableProperty] private bool _isBusy;

    public bool IsDone => State == CheckState.Done;
    public bool IsTodo => State == CheckState.Todo;
    public bool IsBlocked => State == CheckState.Blocked;
    public bool HasFix => HasFixButton && State != CheckState.Done;
    public bool HasLive => Live.Length > 0;
    public bool IsMarked => !IsNote;

    /// <summary>The mark in front of the line: a tick when done, an exclamation mark when it is to do, a dash when it cannot be done now.</summary>
    public string Mark => IsNote ? string.Empty : State switch { CheckState.Done => "✓", CheckState.Todo => "!", _ => "–" };

    public void Update(EasyCheck check)
    {
        Title = check.Title;
        Subtitle = check.Subtitle;
        State = check.State;
        FixLabel = check.Fix?.Label ?? string.Empty;
        HasFixButton = check.Fix is not null;
        IsOptional = check.Optional;
        Live = check.Live;
        IsNote = check.Note;
        FixCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Brings a shown list up to date: the lines are updated in place when the same lines are still wanted (so they tick without a redraw), else the list is made again.</summary>
    public static void Sync(ObservableCollection<EasyCheckItem> items, IReadOnlyList<EasyCheck> checks, Func<string, Task> run)
    {
        if (items.Select(i => i.Id).SequenceEqual(checks.Select(c => c.Id)))
        {
            for (var i = 0; i < items.Count; i++) items[i].Update(checks[i]);
            return;
        }
        items.Clear();
        foreach (var check in checks) items.Add(new EasyCheckItem(check, run));
    }

    partial void OnIsBusyChanged(bool value) => FixCommand.NotifyCanExecuteChanged();
}

/// <summary>One word of the progress strip ("Goal · Sound · Setup · Done"): ticked once passed, accented while it is the screen shown.</summary>
public sealed partial class EasyStripItem : ObservableObject
{
    public EasyStripItem(string title) => Title = title;

    public string Title { get; }

    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _isCurrent;
}
