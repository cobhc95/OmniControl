using OmniControl.Core.Engine;
using OmniControl.App.Shared.Services;

namespace OmniControl.App.Shared.Scripts;

/// The programs a picker offers, read ONCE off the UI thread when it opens: the running ones, the remembered ones and the installed ones,
/// each once, by name. It never changes afterwards (a list that followed the running processes kept moving under the pointer);
/// <see cref="Ready"/> is raised on the UI thread when the one read finished.
public sealed class ProgramSnapshot
{
    private volatile IReadOnlyList<string> _names = Array.Empty<string>();

    /// <param name="remembered">The remembered program paths or names, taken on the UI thread by the caller.</param>
    public ProgramSnapshot(IReadOnlyList<string> remembered) =>
        Guard.OffUi("reading the programs to offer", () =>
        {
            _names = ProgramNames.Known(remembered.Concat(InstalledPrograms.Find().Select(p => p.Path)));
            Dispatcher.UIThread.Post(() => Ready?.Invoke());
        });

    /// The programs read (empty until <see cref="Ready"/>).
    public IReadOnlyList<string> Names => _names;

    public event Action? Ready;
}
