using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Mouse;

/// The real system pointer shown while a mouse mode runs (the "Pointer while held" setting of an action). <see cref="Clear"/>
/// puts the user's own pointers back; nothing stays changed, and start-up resets any pointer a crash left behind.
public sealed class PointerLook : IDisposable
{
    private bool _shapeShown;

    public PointerLook() => PointerShape.Reset();

    /// Shows the pointer named by <paramref name="choice"/> (a <see cref="MouseOptions"/> pointer choice); "keep my pointer" does nothing.
    public void Apply(string choice)
    {
        if (MouseOptions.ShapeOf(choice) is { } shape && PointerShape.Show(shape)) _shapeShown = true;
    }

    /// Puts the user's pointers back.
    public void Clear()
    {
        if (!_shapeShown) return;
        _shapeShown = false;
        PointerShape.Reset();
    }

    public void Dispose() => Clear();
}
