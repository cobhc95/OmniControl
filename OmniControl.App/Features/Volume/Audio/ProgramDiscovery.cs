namespace OmniControl.App.Features.Volume.Audio;

/// The programs installed on this PC (<see cref="InstalledPrograms"/>), read off the UI thread and kept for
/// <see cref="Timing.InstalledProgramsCache"/>: asking again inside that time reads nothing new. Read only.
public sealed class ProgramDiscovery : IDisposable
{
    private readonly ThrottledRun _look;
    private volatile IReadOnlyList<FoundProgram> _installed = Array.Empty<FoundProgram>();

    /// Raised on the UI thread when a look finished.
    public event Action? Changed;

    public ProgramDiscovery() => _look = new ThrottledRun(Timing.InstalledProgramsCache, Look);

    /// What the last look found (empty until the first look finished).
    public IReadOnlyList<FoundProgram> Installed => _installed;

    /// Asks for a look (at most one per cache time; a request inside it is kept for when it is over).
    public void Refresh() => _look.Request();

    private void Look()
    {
        _installed = InstalledPrograms.Find();
        Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }

    public void Dispose() => _look.Dispose();
}
