using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.OtherPcs;

/// <summary>The PC a wake set-up window is about: a paired PC (asked over the link) or this PC (read and changed here). The window knows only this.</summary>
public interface IWakeTarget
{
    string Name { get; }

    /// <summary>True for this PC: its window says "Checking…" and "Waiting for your OK…" instead of naming a PC it asks.</summary>
    bool IsOwn { get; }

    bool IsOnline { get; }

    /// <summary>Why nothing can be changed now (one sentence).</summary>
    string OfflineNote { get; }

    WakeFacts? Facts { get; }

    /// <summary>True when the "Test wake" row is offered (a PC cannot wake itself).</summary>
    bool CanTest { get; }

    /// <summary>True when the facts are read as the window opens (this PC's own, which may never have been read).</summary>
    bool ReadOnOpen { get; }

    event Action Changed;

    Task<WakeReply> CheckAsync(CancellationToken stop);

    Task<WakeReply> FixAsync(IReadOnlyList<WakeFix> fixes, CancellationToken stop);

    /// <summary>Puts back what OmniControl turned on there (it was recorded before each change).</summary>
    Task<WakeReply> UndoAsync();

    Task<string> TestAsync();
}

/// <summary>A paired PC: everything goes through <see cref="OtherPcsService"/>, behind that PC's own prompt.</summary>
public sealed class PairedWakeTarget : IWakeTarget
{
    private readonly OtherPcsService _service;
    private readonly string _id;

    public PairedWakeTarget(string pcId, OtherPcsService service)
    {
        _id = pcId;
        _service = service;
    }

    public string Name => _service.Find(_id)?.DisplayName ?? string.Empty;

    public bool IsOwn => false;

    public bool IsOnline => _service.IsOnline(_id);

    public string OfflineNote => _service.AwayOf(_id) is { } gone
        ? gone.Away == WakeAdvices.Shutdown ? $"{Name} is shut down." : $"{Name} is asleep."
        : $"{Name} is off or cannot be reached.";

    public WakeFacts? Facts => _service.WakeFactsOf(_id);

    public bool CanTest => true;

    public bool ReadOnOpen => false;

    public event Action Changed
    {
        add => _service.Changed += value;
        remove => _service.Changed -= value;
    }

    public Task<WakeReply> CheckAsync(CancellationToken stop) => _service.CheckWakeAsync(_id, stop);

    public Task<WakeReply> FixAsync(IReadOnlyList<WakeFix> fixes, CancellationToken stop) => _service.FixWakeAsync(_id, fixes, stop);

    public Task<WakeReply> UndoAsync() => _service.UndoWakeAsync(_id);

    public Task<string> TestAsync() => _service.TestWakeAsync(_id);
}

/// <summary>This PC: read and fixed here, behind Windows' own administrator prompt (the same fixer a paired PC's ask ends in).</summary>
public sealed class OwnWakeTarget : IWakeTarget
{
    /// <summary>What this PC is called in its own set-up window and on the page's title line.</summary>
    public const string Words = "This PC";

    private readonly OtherPcsService _service;

    public OwnWakeTarget(OtherPcsService service) => _service = service;

    public string Name => Words;

    public bool IsOwn => true;

    public bool IsOnline => true;

    public string OfflineNote => string.Empty;

    public WakeFacts? Facts => _service.OwnWakeFacts;

    public bool CanTest => false;

    public bool ReadOnOpen => true;

    public event Action Changed
    {
        add => _service.Changed += value;
        remove => _service.Changed -= value;
    }

    public Task<WakeReply> CheckAsync(CancellationToken stop) => _service.CheckOwnWakeAsync(stop);

    public Task<WakeReply> FixAsync(IReadOnlyList<WakeFix> fixes, CancellationToken stop) => _service.FixOwnWakeAsync(fixes, stop);

    public Task<WakeReply> UndoAsync() => _service.UndoOwnWakeAsync();

    public Task<string> TestAsync() => Task.FromResult("This PC cannot wake itself.");
}
