namespace OmniControl.App.Shared.Services;

/// Runs a view's reaction at most once per frame its window draws: any number of requests between two frames (a 1000 Hz mouse sends about
/// sixteen pointer moves per frame) cost one reaction, made just before the frame that shows it, so nothing is shown later than before.
/// Without a window (not shown yet, or drawn into a bitmap) it runs at once.
public sealed class OncePerFrame
{
    private readonly Visual _owner;
    private readonly string _what;
    private readonly Action _react;
    private bool _waiting;

    /// <param name="owner">The visual whose window's frames count.</param>
    /// <param name="what">What the reaction is, for the errors log.</param>
    public OncePerFrame(Visual owner, string what, Action react)
    {
        _owner = owner;
        _what = what;
        _react = react;
    }

    public void Request()
    {
        if (_waiting) return;
        if (TopLevel.GetTopLevel(_owner) is not { } window)
        {
            _react();
            return;
        }
        _waiting = true;
        window.RequestAnimationFrame(_ =>
        {
            _waiting = false;
            Guard.Run(_what, _react);
        });
    }
}
