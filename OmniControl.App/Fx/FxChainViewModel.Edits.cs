using CommunityToolkit.Mvvm.Input;

namespace OmniControl.App.Fx;

/// <summary>
/// The selection of a chain and what acts on it (the model of TabForge's FX chain list, same owner): Delete removes every selected
/// effect, power (Space, a row's switch, the toolbar's On) switches them all, Alt+Up/Down and dragging move them together, Ctrl+C /
/// Ctrl+V copy and paste them, Esc reduces the selection to one. Selecting itself (click, Ctrl+click, Shift+click, Shift+arrows,
/// Ctrl+A, a rubber band) is the list's own and <see cref="RubberBandSelect"/>'s; the view reports it through <see cref="SetPicked"/>.
/// </summary>
public sealed partial class FxChainViewModel
{
    private List<FxSlotViewModel> _picked = new();
    private bool _switching, _reordering;

    /// <summary>Raised when the view should select these effects again (after a move or paste rebuilt the list's selection).</summary>
    public event Action<IReadOnlyList<FxSlotViewModel>>? ReselectRequested;

    /// <summary>The effects highlighted in the list. The editor shows the first.</summary>
    public void SetPicked(IEnumerable<FxSlotViewModel> picked) => _picked = picked.ToList();

    private List<FxSlotViewModel> Targets() =>
        (_picked.Count > 0 ? _picked : Selected is { } s ? new List<FxSlotViewModel> { s } : new()).OrderBy(Slots.IndexOf).ToList();

    /// <summary>Space: the highlighted effects go on or off together (all off if the first is on).</summary>
    public void ToggleEnabled()
    {
        var t = Targets();
        if (t.Count > 0) SwitchAll(t, !t[0].Enabled);
    }

    /// <summary>One selected effect was switched (its row's switch or the toolbar's On): the rest of the selection follows.</summary>
    private void EnabledChanged(FxSlotViewModel slot)
    {
        if (!_switching && _picked.Count > 1 && _picked.Contains(slot)) SwitchAll(_picked, slot.Enabled);
    }

    private void SwitchAll(IEnumerable<FxSlotViewModel> slots, bool on)
    {
        _switching = true;
        foreach (var s in slots.ToList()) s.Enabled = on;
        _switching = false;
    }

    [RelayCommand]
    private void Remove()
    {
        var targets = Targets();
        if (targets.Count == 0) return;
        var at = Slots.IndexOf(targets[0]);
        _editors.Leaving(targets.Select(t => t.Slot));
        foreach (var s in targets)
        {
            Untrack(s);
            _chain.Remove(s.Slot);
            Slots.Remove(s);
        }
        _picked.Clear();
        Selected = Slots.Count == 0 ? null : Slots[Math.Min(at, Slots.Count - 1)];
        if (Selected is { } next) ReselectRequested?.Invoke(new[] { next });
        Changed();
    }

    [RelayCommand] private void MoveUp() => Step(-1);
    [RelayCommand] private void MoveDown() => Step(1);

    /// <summary>Moves the highlighted effects one place up or down, keeping their order; effects at the edge stay put.</summary>
    private void Step(int delta)
    {
        var targets = Targets();
        if (targets.Count == 0) return;
        var order = Slots.ToList();
        foreach (var s in delta < 0 ? targets : Enumerable.Reverse(targets))
        {
            var from = order.IndexOf(s);
            var to = from + delta;
            if (to < 0 || to >= order.Count || targets.Contains(order[to])) continue;
            order.RemoveAt(from);
            order.Insert(to, s);
        }
        Reorder(order, targets);
    }

    /// <summary>
    /// Drag and drop: the effects at <paramref name="rows"/> go, in their order, in front of the <paramref name="insertAt"/>-th of the
    /// effects that stay (their count: the end). The sound then passes them in the new order; each keeps its state (matched by slot).
    /// </summary>
    public void MoveRows(IReadOnlyList<int> rows, int insertAt)
    {
        var moving = rows.Where(i => i >= 0 && i < Slots.Count).Distinct().OrderBy(i => i).Select(i => Slots[i]).ToList();
        if (moving.Count == 0) return;
        var rest = Slots.Where(s => !moving.Contains(s)).ToList();
        insertAt = Math.Clamp(insertAt, 0, rest.Count);
        Reorder(rest.Take(insertAt).Concat(moving).Concat(rest.Skip(insertAt)).ToList(), moving);
    }

    /// <summary>Puts the list and the stored chain in <paramref name="order"/> (both, always the same order) and selects <paramref name="keep"/> again.</summary>
    private void Reorder(List<FxSlotViewModel> order, IReadOnlyList<FxSlotViewModel> keep)
    {
        if (order.SequenceEqual(Slots)) return;
        var selected = Selected;
        _reordering = true;
        for (var i = 0; i < order.Count; i++)
        {
            var from = Slots.IndexOf(order[i]);
            if (from != i) Slots.Move(from, i);
        }
        _chain.Clear();
        _chain.AddRange(order.Select(s => s.Slot));
        Selected = selected;
        _reordering = false;
        Changed();
        ReselectRequested?.Invoke(keep);
    }

    [RelayCommand]
    private void Copy()
    {
        var targets = Targets();
        if (targets.Count > 0) _clipboard.Copy(targets.Select(t => t.Slot));
    }

    /// <summary>Puts copies of the copied effects after the last highlighted effect (or at the end) and selects them.</summary>
    [RelayCommand]
    private void Paste()
    {
        if (!_clipboard.HasEffects) return;
        var targets = Targets();
        var at = targets.Count > 0 ? Slots.IndexOf(targets[^1]) + 1 : Slots.Count;
        var added = new List<FxSlotViewModel>();
        foreach (var slot in _clipboard.Paste())
        {
            _chain.Insert(at, slot);
            var vm = Track(new FxSlotViewModel(slot, SaveChain));
            Slots.Insert(at++, vm);
            added.Add(vm);
        }
        Selected = added[0];
        Changed();
        ReselectRequested?.Invoke(added);
    }

    /// <summary>Esc: a multi-selection becomes the effect the editor shows. False when there was nothing to reduce.</summary>
    public bool CollapseSelection()
    {
        if (_picked.Count < 2 || Selected is not { } s) return false;
        ReselectRequested?.Invoke(new[] { s });
        return true;
    }
}
