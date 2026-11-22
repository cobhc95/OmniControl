namespace OmniControl.App.Shell;

/// Decides which pages' views the main window keeps built. Off ("Free pages you leave"): every visited page. On: the open page and the
/// last <see cref="Recent"/> visited, and any other page until it has been left as long as "Free memory after closing" says (Never: none
/// is let go). Only the views go; what the user set lives in the pages' view-models.
internal sealed class PageViewKeeper : IDisposable
{
    private const int Recent = 2;

    private sealed class Visit
    {
        public Visit(PageViewModel page) => Page = page;
        public PageViewModel Page { get; }
        public long Left { get; set; } // Environment.TickCount64 when another page opened
    }

    private readonly List<Visit> _visits = new(); // the open page first, then by how recently each was left
    private readonly Func<bool> _on;
    private readonly Func<TimeSpan?> _after;
    private readonly Action<IReadOnlyList<PageViewModel>> _kept;
    private readonly DispatcherTimer _due = new();
    private IReadOnlyList<PageViewModel> _published = Array.Empty<PageViewModel>();

    /// <param name="on">Whether pages are freed now.</param>
    /// <param name="after">How long a page stays built once left; null = never let go.</param>
    /// <param name="kept">Told which pages to keep built whenever that changes.</param>
    public PageViewKeeper(Func<bool> on, Func<TimeSpan?> after, Action<IReadOnlyList<PageViewModel>> kept)
    {
        (_on, _after, _kept) = (on, after, kept);
        _due.Tick += OnDue;
    }

    /// A page was opened. Visits are noted also while the setting is off, so switching it on knows the order.
    public void Opened(PageViewModel page)
    {
        if (_visits.Count > 0 && _visits[0].Page != page) _visits[0].Left = Environment.TickCount64;
        _visits.RemoveAll(v => v.Page == page);
        _visits.Insert(0, new Visit(page));
        Refresh();
    }

    /// The page is gone (its feature was switched off): its view is let go whatever the setting says.
    public void Forget(PageViewModel page)
    {
        if (_visits.RemoveAll(v => v.Page == page) > 0) Refresh();
    }

    private void OnDue(object? sender, EventArgs e) => Guard.Run("Free pages you left", Refresh);

    private void Refresh()
    {
        _due.Stop();
        var wait = double.MaxValue;
        if (_on() && _after() is { } after)
        {
            var now = Environment.TickCount64;
            for (var i = _visits.Count - 1; i > Recent; i--)
            {
                var remaining = _visits[i].Left + after.TotalMilliseconds - now;
                if (remaining <= 0) _visits.RemoveAt(i);
                else wait = Math.Min(wait, remaining);
            }
        }
        var kept = _visits.Select(v => v.Page).ToList();
        if (!_published.SequenceEqual(kept))
        {
            _published = kept;
            _kept(kept);
        }
        if (wait < double.MaxValue)
        {
            _due.Interval = TimeSpan.FromMilliseconds(wait);
            _due.Start();
        }
    }

    public void Dispose()
    {
        _due.Stop();
        _due.Tick -= OnDue;
    }
}
