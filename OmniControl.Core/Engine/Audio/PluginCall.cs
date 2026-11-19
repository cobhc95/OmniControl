namespace OmniControl.Core.Engine;

/// The one guard around a call into a VST3 plug-in's controller or view that may fail inside the plug-in.
internal static class PluginCall
{
    /// Runs <paramref name="call"/>; a failure inside the plug-in (or a plug-in already closed) gives <paramref name="fallback"/>.
    public static T Ask<T>(Func<T> call, T fallback)
    {
        try
        {
            return call();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException or ObjectDisposedException)
        {
            return fallback; // a plug-in that refuses a call keeps what it had; the editor and the chain stay usable
        }
    }
}
