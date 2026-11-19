namespace OmniControl.Core.Engine;

/// A VST2 plug-in's own editor (effEditOpen into the host's child window; see <see cref="IPluginEditor"/>). VST2 editors have a fixed
/// size the plug-in may change (audioMasterSizeWindow, raised as <see cref="Resized"/> from <see cref="Idle"/>), need the host's idle
/// call while open (effEditIdle), and have no content scale: they draw at their own size, so a scale other than the display's own is refused.
internal sealed class Vst2Editor : IPluginEditor
{
    /// Two content scales closer than this are the same (float rounding).
    private const double ScaleTolerance = 0.001;

    private readonly Vst2Effect _effect;
    private IntPtr _parent;
    private double _scale = 1;
    private bool _disposed;

    internal Vst2Editor(Vst2Effect effect) => _effect = effect;

    public event Action<int, int>? Resized;

    public (int Width, int Height) Size => _disposed ? (0, 0) : _effect.EditorSize();

    public bool IsOpen => !_disposed;

    /// VST2 has no way for a host to offer an editor a size.
    public bool CanResize => false;

    public void AttachTo(IntPtr parentWindow, double scale)
    {
        if (_disposed) return;
        _effect.OpenEditor(this, parentWindow);
        _parent = parentWindow;
        _scale = scale;
    }

    public void Detach()
    {
        if (_disposed || _parent == IntPtr.Zero) return;
        _effect.CloseEditor(this);
        _parent = IntPtr.Zero;
    }

    public (int Width, int Height) Offer(int width, int height) => Size;

    /// A VST2 editor keeps its own size: only the scale it was attached at counts as taken.
    public bool TryScale(double scale) => !_disposed && Math.Abs(scale - _scale) < ScaleTolerance;

    public void MoveView(int x, int y)
    {
        if (!_disposed) PluginWindow.MoveFirstChild(_parent, x, y);
    }

    public void Idle()
    {
        if (_disposed || _parent == IntPtr.Zero) return;
        _effect.EditorIdle();
        if (_effect.TakePendingSize(out var width, out var height) && width > 0 && height > 0) Resized?.Invoke(width, height);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Detach();
        _disposed = true;
    }
}

/// The editor calls of a VST2 plug-in (UI thread). A plug-in has one editor window at a time: opening another closes the first.
internal sealed unsafe partial class Vst2Effect
{
    /// The size used when a plug-in does not say its editor's size (pixels).
    private const int UnknownEditorWidth = 400, UnknownEditorHeight = 300;

    private Vst2Editor? _openEditor;

    /// The editor's size as the plug-in reports it now (effEditGetRect; many plug-ins know it only once the editor is open).
    internal (int Width, int Height) EditorSize()
    {
        if (_disposed) return (0, 0);
        Vst2Abi.ERect* rect = null;
        Dispatch(Vst2Abi.EffEditGetRect, 0, 0, (IntPtr)(&rect), 0);
        if (rect is null || rect->right <= rect->left || rect->bottom <= rect->top) return (UnknownEditorWidth, UnknownEditorHeight);
        return (rect->right - rect->left, rect->bottom - rect->top);
    }

    internal void OpenEditor(Vst2Editor editor, IntPtr parent)
    {
        if (_disposed) throw new InvalidOperationException("The plug-in is closed.");
        if (_openEditor is { } other && !ReferenceEquals(other, editor)) other.Detach();
        _openEditor = editor;
        Dispatch(Vst2Abi.EffEditOpen, 0, 0, parent, 0); // its answer is not reliable (many plug-ins answer 0 after opening): not read
    }

    internal void CloseEditor(Vst2Editor editor)
    {
        if (!ReferenceEquals(_openEditor, editor)) return;
        _openEditor = null;
        if (!_disposed) Dispatch(Vst2Abi.EffEditClose, 0, 0, IntPtr.Zero, 0);
    }

    /// Closes an editor still open when the plug-in is disposed.
    private void CloseEditor()
    {
        if (_openEditor is not { } editor) return;
        _openEditor = null;
        editor.Dispose();
        Dispatch(Vst2Abi.EffEditClose, 0, 0, IntPtr.Zero, 0);
    }

    internal void EditorIdle()
    {
        if (!_disposed && _openEditor is not null) Dispatch(Vst2Abi.EffEditIdle, 0, 0, IntPtr.Zero, 0);
    }
}
