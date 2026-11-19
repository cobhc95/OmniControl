namespace OmniControl.App.Features.Devices.Pages;

/// One numbered stage of a device's set-up: its number, title, whether it is done (asked
/// of the device each refresh), the muted summary shown while it is folded, and whether it is the one stage that is open.
public sealed partial class SetupStageItem : ObservableObject
{
    private readonly Func<bool> _done;
    private readonly Func<string> _summaryOf;
    private readonly Action<SetupStageItem> _open;

    public SetupStageItem(int number, string title, Func<bool> done, Func<string> summary, Action<SetupStageItem> open, string key = "", string text = "", string confirmLabel = "", Action? confirm = null)
    {
        Key = key;
        Text = text;
        ConfirmLabel = confirmLabel;
        ConfirmCommand = new RelayCommand(() => confirm?.Invoke());
        Number = number;
        Title = title;
        _done = done;
        _summaryOf = summary;
        _open = open;
    }

    /// Which stage it is ("python", "signin"...; a template's own stage: "template:" and its id), so the view finds it whatever its number.
    public string Key { get; }

    /// A template stage's instruction and its confirm pill (empty = none).
    public string Text { get; }
    public string ConfirmLabel { get; }
    public System.Windows.Input.ICommand ConfirmCommand { get; }

    public int Number { get; }
    public string NumberText => Number.ToString();
    public string Title { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsCurrent))] private bool _isOpen;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsCurrent))] private bool _isDone;
    [ObservableProperty] private string _summary = string.Empty;

    /// The open stage that is still to do: its number tag is the caution one.
    public bool IsCurrent => IsOpen && !IsDone;

    public void Refresh()
    {
        IsDone = _done();
        Summary = _summaryOf();
    }

    [RelayCommand]
    private void Open() => _open(this);
}

/// A set-up's stages: one open at a time, the first that is not done when the set-up opens and after a stage is finished by its own action.
public sealed class SetupStages
{
    public SetupStages(Func<Action<SetupStageItem>, IReadOnlyList<SetupStageItem>> make) => Items = make(Open);

    public IReadOnlyList<SetupStageItem> Items { get; }

    public bool AllDone => Items.All(s => s.IsDone);

    public void Refresh()
    {
        foreach (var stage in Items) stage.Refresh();
    }

    public void Open(SetupStageItem stage)
    {
        foreach (var other in Items) other.IsOpen = other == stage;
    }

    /// Opens the first stage that is not done (the last one when all are).
    public void OpenNext()
    {
        Refresh();
        Open(Items.FirstOrDefault(s => !s.IsDone) ?? Items[^1]);
    }

    /// Opens the stage with this number.
    public void Open(int number) => Open(Items.First(s => s.Number == number));
}
