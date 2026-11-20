using Avalonia.Input;

namespace OmniControl.App.Shared.Services;

/// THE undo and redo of a window: named steps, each with how to take it back and do it again. A step recorded with the same merge key
/// as the newest one within <see cref="Timing.UndoMergeWindow"/> of it joins it (a drag or typing is one step); at most
/// <see cref="Timing.UndoLimit"/> steps are kept. Steps recorded while a step is being undone or redone are ignored, so the owner may
/// undo through its normal paths. The window opts in with <c>Controls/EditMenu</c>; clear it when the window closes.
public sealed partial class UndoHistory : ObservableObject
{
    private sealed record Step(string Name, Action Undo, Action Redo, object? MergeKey, Action? Forget, long At);

    private readonly LinkedList<Step> _done = new();
    private readonly Stack<Step> _undone = new();
    private readonly int _limit;
    private readonly Func<long> _now;
    private bool _applying, _canMerge;

    /// <param name="now">Milliseconds (tests set the clock).</param>
    public UndoHistory(int limit = Timing.UndoLimit, Func<long>? now = null)
    {
        _limit = limit;
        _now = now ?? (() => Environment.TickCount64);
    }

    /// A step is being undone or redone now.
    public bool IsApplying => _applying;

    public bool CanUndo => _done.Count > 0;
    public bool CanRedo => _undone.Count > 0;

    /// The Edit menu's words: "Undo Remove Compressor", or "Undo" when there is nothing.
    public string UndoText => _done.Last is { } last ? "Undo " + last.Value.Name : "Undo";
    public string RedoText => _undone.TryPeek(out var next) ? "Redo " + next.Name : "Redo";

    /// <param name="forget">Called if the step leaves while undone (a new step replaced it, or the history was cleared), to release what it holds.</param>
    public void Record(string name, Action undo, Action redo, object? mergeKey = null, Action? forget = null)
    {
        if (_applying) return;
        DropUndone();
        var now = _now();
        if (_canMerge && mergeKey is not null && _done.Last is { } last && Equals(last.Value.MergeKey, mergeKey)
            && now - last.Value.At <= (long)Timing.UndoMergeWindow.TotalMilliseconds)
            last.Value = last.Value with { Redo = redo, At = now };
        else
        {
            _done.AddLast(new Step(name, undo, redo, mergeKey, forget, now));
            if (_done.Count > _limit) _done.RemoveFirst();
        }
        _canMerge = true;
        Notify();
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    public void Undo()
    {
        if (_done.Last is not { } last) return;
        _done.RemoveLast();
        Apply(last.Value.Undo);
        _undone.Push(last.Value);
    }

    [RelayCommand(CanExecute = nameof(CanRedo))]
    public void Redo()
    {
        if (!_undone.TryPop(out var step)) return;
        Apply(step.Redo);
        _done.AddLast(step with { At = _now() });
    }

    /// THE undo keys: Ctrl+Z undoes, Ctrl+Y or Ctrl+Shift+Z redoes. True when the key was one of them.
    public bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (modifiers == KeyModifiers.Control && key == Key.Z) Undo();
        else if ((modifiers == KeyModifiers.Control && key == Key.Y) || (modifiers == (KeyModifiers.Control | KeyModifiers.Shift) && key == Key.Z)) Redo();
        else return false;
        return true;
    }

    /// Forgets every step (the window closed).
    public void Clear()
    {
        DropUndone();
        _done.Clear();
        Notify();
    }

    private void Apply(Action action)
    {
        _applying = true;
        try { action(); }
        finally { _applying = false; }
        _canMerge = false; // the next change is a step of its own
        Notify();
    }

    private void DropUndone()
    {
        while (_undone.TryPop(out var step)) step.Forget?.Invoke();
    }

    private void Notify()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(UndoText));
        OnPropertyChanged(nameof(RedoText));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }
}
