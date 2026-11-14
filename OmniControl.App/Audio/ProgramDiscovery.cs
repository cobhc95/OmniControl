using Avalonia.Threading;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Audio;

/// <summary>
/// The programs installed on this PC (<see cref="InstalledPrograms"/>), read off the UI thread and kept for
/// <see cref="Timing.InstalledProgramsCache"/>: asking again inside that time reads nothing new. Read only.
/// </summary>
public sealed class ProgramDiscovery : IDisposable
{
    private readonly ThrottledRun _look;
    private volatile IReadOnlyList<FoundProgram> _installed = Array.Empty<FoundProgram>();

    /// <summary>Raised on the UI thread when a look finished.</summary>
    public event Action? Changed;

    public ProgramDiscovery() => _look = new ThrottledRun(Timing.InstalledProgramsCache, Look);

    /// <summary>What the last look found (empty until the first look finished).</summary>
    public IReadOnlyList<FoundProgram> Installed => _installed;

    /// <summary>Asks for a look (at most one per cache time; a request inside it is kept for when it is over).</summary>
    public void Refresh() => _look.Request();

    private void Look()
    {
        _installed = InstalledPrograms.Find();
        Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }

    public void Dispose() => _look.Dispose();
}
