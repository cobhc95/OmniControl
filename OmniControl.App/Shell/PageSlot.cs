namespace OmniControl.App.Shell;

/// One page of the sidebar or the footer: what the shell shows of it (<see cref="Info"/>) and its view-model, made the first time something
/// needs it (<see cref="Page"/>) and then kept for the run. A page that fails to be made is logged and said once, and stays empty.
public sealed class PageSlot : ObservableObject
{
    private readonly Func<PageViewModel> _make;
    private readonly Action<PageViewModel> _made;
    private readonly Action<string> _report;
    private PageViewModel? _page;
    private bool _failed;
    private bool _isOpen;

    /// <param name="made">Wires a page just made into the shell (its Restore defaults button).</param>
    /// <param name="report">Says the one sentence when the page cannot be made.</param>
    public PageSlot(PageInfo info, Func<PageViewModel> make, Action<PageViewModel> made, Action<string> report) =>
        (Info, _make, _made, _report) = (info, make, made, report);

    public PageInfo Info { get; }

    public string Title => Info.Title;

    public string Icon => Info.Icon;

    /// True while this is the page shown on the right (a footer button uses it for its selected look).
    public bool IsOpen { get => _isOpen; set => SetProperty(ref _isOpen, value); }

    /// The view-model if it has been made; never makes it.
    public PageViewModel? IfMade => _page;

    /// The view-model, made now if it was not yet; null when it could not be made.
    public PageViewModel? Page
    {
        get
        {
            if (_page is not null || _failed) return _page;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _page = _make();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _failed = true;
                Guard.Log($"Page {Title}: made", ex);
                _report(Guard.SeeLog($"The {Title} page couldn't start."));
                return null;
            }
            UiTiming.Write($"Page {Title}: made {clock.ElapsedMilliseconds} ms");
            _made(_page);
            return _page;
        }
    }
}
