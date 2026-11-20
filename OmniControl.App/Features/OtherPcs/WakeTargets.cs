using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.Features.OtherPcs;

/// The PC a wake set-up window is about: a paired PC (asked over the link) or this PC (read and changed here). The window knows only this.
public interface IWakeTarget
{
    string Name { get; }

    /// True for this PC: its window says "Checking…" and "Waiting for your OK…" instead of naming a PC it asks.
    bool IsOwn { get; }

    bool IsOnline { get; }

    /// Why nothing can be changed now (one sentence).
    string OfflineNote { get; }

    WakeFacts? Facts { get; }

    /// True when the "Test wake" row is offered (a PC cannot wake itself).
    bool CanTest { get; }

    /// True when the facts are read as the window opens (this PC's own, which may never have been read).
    bool ReadOnOpen { get; }

    event Action Changed;

    Task<WakeReply> CheckAsync(CancellationToken stop);

    Task<WakeReply> FixAsync(IReadOnlyList<WakeFix> fixes, CancellationToken stop);

    /// Puts back what OmniControl turned on there (it was recorded before each change).
    Task<WakeReply> UndoAsync();

    Task<string> TestAsync();
}

/// A paired PC: everything goes through <see cref="PcWake"/>, behind that PC's own prompt.
public sealed class PairedWakeTarget : IWakeTarget
{
    private readonly OtherPcsService _service;
    private readonly PcWake _wake;
    private readonly string _id;

    public PairedWakeTarget(string pcId, OtherPcsService service, PcWake wake)
    {
        _id = pcId;
        _service = service;
        _wake = wake;
    }

    public string Name => _service.Find(_id)?.DisplayName ?? string.Empty;

    public bool IsOwn => false;

    public bool IsOnline => _service.IsOnline(_id);

    public string OfflineNote => _wake.AwayOf(_id) is { } gone
        ? gone.Away == WakeAdvices.Shutdown ? $"{Name} is shut down." : $"{Name} is asleep."
        : $"{Name} is off or cannot be reached.";

    public WakeFacts? Facts => _wake.WakeFactsOf(_id);

    public bool CanTest => true;

    public bool ReadOnOpen => false;

    public event Action Changed
    {
        add => _service.Changed += value;
        remove => _service.Changed -= value;
    }

    public Task<WakeReply> CheckAsync(CancellationToken stop) => _wake.CheckWakeAsync(_id, stop);

    public Task<WakeReply> FixAsync(IReadOnlyList<WakeFix> fixes, CancellationToken stop) => _wake.FixWakeAsync(_id, fixes, stop);

    public Task<WakeReply> UndoAsync() => _wake.UndoWakeAsync(_id);

    public Task<string> TestAsync() => _wake.TestWakeAsync(_id, _wake.GrantTest(_id));
}

/// This PC: read and fixed here, behind Windows' own administrator prompt (the same fixer a paired PC's ask ends in).
public sealed class OwnWakeTarget : IWakeTarget
{
    /// What this PC is called in its own set-up window and on the page's title line.
    public const string Words = "This PC";

    private readonly OtherPcsService _service;
    private readonly PcWake _wake;

    public OwnWakeTarget(OtherPcsService service, PcWake wake)
    {
        _service = service;
        _wake = wake;
    }

    public string Name => Words;

    public bool IsOwn => true;

    public bool IsOnline => true;

    public string OfflineNote => string.Empty;

    public WakeFacts? Facts => _wake.OwnWakeFacts;

    public bool CanTest => false;

    public bool ReadOnOpen => true;

    public event Action Changed
    {
        add => _service.Changed += value;
        remove => _service.Changed -= value;
    }

    public Task<WakeReply> CheckAsync(CancellationToken stop) => _wake.CheckOwnWakeAsync(stop);

    public Task<WakeReply> FixAsync(IReadOnlyList<WakeFix> fixes, CancellationToken stop) => _wake.FixOwnWakeAsync(fixes, stop);

    public Task<WakeReply> UndoAsync() => _wake.UndoOwnWakeAsync();

    public Task<string> TestAsync() => Task.FromResult("This PC cannot wake itself.");
}
