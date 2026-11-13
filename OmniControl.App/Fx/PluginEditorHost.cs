using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using OmniControl.Core.Engine;

namespace OmniControl.App.Fx;

/// <summary>
/// The native child window a plug-in's own editor is attached to (Avalonia's default NativeControlHost child). It only attaches
/// and detaches: its size comes from <see cref="PluginEditorArea"/>, and whoever opened the editor disposes it.
/// </summary>
internal sealed class PluginEditorHost : NativeControlHost
{
    private readonly IPluginEditor _editor;

    public PluginEditorHost(IPluginEditor editor) => _editor = editor;

    /// <summary>The editor could not be shown (the reason, one sentence). Raised while the native window is being made.</summary>
    public event Action<string>? Failed;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var child = base.CreateNativeControlCore(parent);
        try
        {
            _editor.AttachTo(child.Handle, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or COMException)
        {
            Failed?.Invoke(ex.Message); // a plug-in that won't embed must never take the window down
        }
        return child;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _editor.Detach(); // both formats want the editor taken out while its parent window still exists
        base.DestroyNativeControlCore(control);
    }
}
