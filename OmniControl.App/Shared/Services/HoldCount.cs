namespace OmniControl.App.Shared.Services;

/// A count of who wants something now (a view on screen that wants fresh values): each <see cref="Take"/> holds it until its handle is
/// disposed (twice is harmless); <see cref="Changed"/> follows <see cref="Held"/> going on or off. UI thread.
public sealed class HoldCount
{
    private int _count;

    /// Someone holds it now.
    public bool Held => _count > 0;

    /// <see cref="Held"/> changed.
    public event Action? Changed;

    public IDisposable Take()
    {
        if (++_count == 1) Changed?.Invoke();
        return new Handle(this);
    }

    private void Drop()
    {
        if (--_count == 0) Changed?.Invoke();
    }

    private sealed class Handle(HoldCount owner) : IDisposable
    {
        private HoldCount? _owner = owner;

        public void Dispose()
        {
            _owner?.Drop();
            _owner = null;
        }
    }
}
