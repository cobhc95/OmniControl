namespace OmniControl.App.Features.Volume.Fx;

/// The larger graph over the FX window's panes: it opens when the pointer rests on the thumbnail for <see cref="Timing.FxGraphHoverOpen"/>
/// and closes when the pointer has left the thumbnail and the graph (after <see cref="Timing.FxGraphHoverClose"/>, so the gap between
/// them can be crossed). A click or Enter on the thumbnail pins it open, a second one closes it, and Esc closes it.
public sealed partial class FxAnalysisViewModel
{
    private readonly DispatcherTimer _openTimer = new() { Interval = Timing.FxGraphHoverOpen };
    private readonly DispatcherTimer _closeTimer = new() { Interval = Timing.FxGraphHoverClose };
    private bool _overThumb, _overPanel, _pinned;
    private bool _isPanelOpen;

    public bool IsPanelOpen
    {
        get => _isPanelOpen;
        private set => SetProperty(ref _isPanelOpen, value);
    }

    /// The pointer came onto the thumbnail or left it.
    public void PointerOverThumb(bool over)
    {
        _overThumb = over;
        HoverChanged();
    }

    /// The pointer came onto the open graph or left it.
    public void PointerOverPanel(bool over)
    {
        _overPanel = over;
        HoverChanged();
    }

    /// A click or Enter on the thumbnail: pins the graph open (opening it if needed), or closes a pinned one.
    public void TogglePin()
    {
        if (_pinned) Close();
        else
        {
            _pinned = true;
            _openTimer.Stop();
            _closeTimer.Stop();
            IsPanelOpen = true;
        }
    }

    /// Esc: closes the graph; false when it was not open (the key then does what it does without it).
    public bool ClosePanel()
    {
        if (!IsPanelOpen) return false;
        Close();
        return true;
    }

    /// The graph's pop-up was closed by its window (the window went away or was hidden).
    public void PanelClosed()
    {
        _pinned = false;
        _overPanel = false;
        IsPanelOpen = false;
    }

    private void HoverChanged()
    {
        if (_overThumb || _overPanel)
        {
            _closeTimer.Stop();
            if (!IsPanelOpen && _overThumb && !_openTimer.IsEnabled) _openTimer.Start();
            return;
        }
        _openTimer.Stop();
        if (IsPanelOpen && !_pinned) _closeTimer.Start();
    }

    private void OnOpenTimer(object? sender, EventArgs e)
    {
        _openTimer.Stop();
        if (_overThumb) IsPanelOpen = true;
    }

    private void OnCloseTimer(object? sender, EventArgs e)
    {
        _closeTimer.Stop();
        if (!_overThumb && !_overPanel && !_pinned) IsPanelOpen = false;
    }

    /// Closes the graph; a closed graph is not under the pointer (its pop-up may go without saying the pointer left).
    private void Close()
    {
        _pinned = false;
        _overPanel = false;
        _openTimer.Stop();
        _closeTimer.Stop();
        IsPanelOpen = false;
    }
}
