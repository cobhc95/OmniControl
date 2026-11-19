namespace OmniControl.App.Features.Screenshot;

/// The drawings on a screenshot in the order they were made, with undo and redo: each added item is one step. Adding after an undo drops
/// what could have been redone; <see cref="Dropped"/> hears of every item that leaves for good, so it can release what it holds.
public sealed class UndoList<T>
{
    private readonly List<T> _items = new();
    private readonly Stack<T> _undone = new();

    /// An item left for good (dropped from the redo list, or cleared).
    public event Action<T>? Dropped;

    public IReadOnlyList<T> Items => _items;
    public bool CanUndo => _items.Count > 0;
    public void Add(T item)
    {
        while (_undone.Count > 0) Dropped?.Invoke(_undone.Pop());
        _items.Add(item);
    }

    /// Takes the newest item away (it can be redone). False when there is none.
    public bool Undo()
    {
        if (_items.Count == 0) return false;
        _undone.Push(_items[^1]);
        _items.RemoveAt(_items.Count - 1);
        return true;
    }

    /// Puts the last undone item back. False when there is none.
    public bool Redo()
    {
        if (_undone.Count == 0) return false;
        _items.Add(_undone.Pop());
        return true;
    }

    /// Drops every item, shown and undone.
    public void Clear()
    {
        foreach (var item in _items) Dropped?.Invoke(item);
        while (_undone.Count > 0) Dropped?.Invoke(_undone.Pop());
        _items.Clear();
    }
}
