namespace OmniControl.App.Features.OtherPcs.Pages;

/// What <see cref="ReachChecklist"/> binds to: the title, the headings and the two lists of rows of "Can't reach it on this network?", made again by <see cref="Show"/>.
public sealed class ReachHelp : ObservableObject
{
    private readonly Action<string> _runFix;
    private ReachChecks _checks = ReachChecks.None;

    /// <param name="runFix">Runs the fix of the row with that id.</param>
    public ReachHelp(Action<string> runFix) => _runFix = runFix;

    public string Title => _checks.Title;

    public string ThisHeading => _checks.ThisHeading;

    public string OtherHeading => _checks.OtherHeading;

    /// A heading shows only over rows, and only when it has words.
    public bool HasThisHeading => ThisPc.Count > 0 && ThisHeading.Length > 0;

    public bool HasOtherHeading => OtherPc.Count > 0 && OtherHeading.Length > 0;

    public ObservableCollection<CheckItem> ThisPc { get; } = new();

    public ObservableCollection<CheckItem> OtherPc { get; } = new();

    public void Show(ReachChecks checks)
    {
        _checks = checks;
        Fill(ThisPc, checks.ThisPc);
        Fill(OtherPc, checks.OtherPc);
        OnPropertyChanged(string.Empty);
    }

    private void Fill(ObservableCollection<CheckItem> items, IReadOnlyList<CheckRow> rows)
    {
        items.Clear();
        foreach (var row in rows) items.Add(new CheckItem(row, _runFix));
    }
}
