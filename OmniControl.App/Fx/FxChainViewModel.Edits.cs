using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Dialogs;
using OmniControl.App.Services;

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

    /// <summary>A folder row stands for itself and its effects (<see cref="FxFolders.BlockOf"/>): they go and move together.</summary>
    private List<FxSlotViewModel> WithChildren(List<FxSlotViewModel> targets)
    {
        var all = new List<FxSlotViewModel>();
        foreach (var t in targets)
        {
            all.Add(t);
            if (!t.IsFolder) continue;
            for (var i = Slots.IndexOf(t) + 1; i < Slots.Count && !Slots[i].IsFolder; i++) all.Add(Slots[i]);
        }
        return all.Distinct().OrderBy(Slots.IndexOf).ToList();
    }

    /// <summary>The question asked before removing: only a device folder that holds effects asks (it goes with them); null when nothing needs asking.</summary>
    private static DialogMessage? RemoveQuestion(List<FxSlotViewModel> targets)
    {
        var full = targets.Where(t => t.Folder is { IsEmpty: false }).ToList();
        return full.Count switch
        {
            0 => null,
            1 => Confirm.Delete("device folder", full[0].Folder!.DeviceName),
            _ => Confirm.Delete($"{full.Count} device folders", null, "device folder"),
        };
    }

    [RelayCommand]
    private async Task RemoveAsync()
    {
        var targets = WithChildren(Targets());
        if (targets.Count == 0) return;
        if (RemoveQuestion(targets) is { } question && !await _ties.Ask(question)) return;
        targets = targets.Where(Slots.Contains).ToList(); // the list may have changed while the question was open
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

    /// <summary>Up/Down in the chain list: the next row that is shown (the effects of a closed folder are skipped), wrapping at both ends. False when the list has no row to go to.</summary>
    public bool StepSelection(int delta)
    {
        var shown = Slots.Where(s => s.Shown).ToList();
        if (shown.Count == 0) return false;
        var at = Selected is { } s ? shown.IndexOf(s) : -1;
        var next = shown[at < 0 ? (delta > 0 ? 0 : shown.Count - 1) : Wrap.Index(at + delta, shown.Count)];
        ReselectRequested?.Invoke(new[] { next });
        return true;
    }

    /// <summary>Left/Right on a device folder's row: closes or opens it. False when the selected row is no folder.</summary>
    public bool SetSelectedFolderOpen(bool open)
    {
        if (Selected?.Folder is not { } folder) return false;
        folder.Expanded = open;
        return true;
    }

    [RelayCommand] private void MoveUp() => Step(-1);
    [RelayCommand] private void MoveDown() => Step(1);

    /// <summary>Moves the highlighted effects one place up or down, keeping their order; effects at the edge stay put.</summary>
    private void Step(int delta)
    {
        var targets = WithChildren(Targets());
        if (targets.Count == 0) return;
        var order = Slots.ToList();
        if (targets[0].IsFolder) { StepFolder(order, targets, delta); return; }
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

    /// <summary>A folder with its effects jumps over the neighbouring folder (the effects before the first folder stay above every folder).</summary>
    private void StepFolder(List<FxSlotViewModel> order, List<FxSlotViewModel> block, int delta)
    {
        var start = order.IndexOf(block[0]);
        var neighbour = delta < 0 ? start - 1 : start + block.Count;
        if (neighbour < 0 || neighbour >= order.Count) return;
        var from = delta < 0 ? order.FindLastIndex(neighbour, s => s.IsFolder) : neighbour;
        if (from < 0) return; // the row above is an effect outside every folder
        var next = delta < 0 ? -1 : order.FindIndex(neighbour + 1, s => s.IsFolder);
        var rest = order.Where(s => !block.Contains(s)).ToList();
        var insertAt = delta < 0 ? from : (next < 0 ? order.Count : next) - block.Count;
        rest.InsertRange(insertAt, block);
        Reorder(rest, block);
    }

    /// <summary>
    /// Drag and drop: the effects at <paramref name="rows"/> go, in their order, in front of the <paramref name="insertAt"/>-th of the
    /// effects that stay (their count: the end). The sound then passes them in the new order; each keeps its state (matched by slot).
    /// </summary>
    public void MoveRows(IReadOnlyList<int> rows, int insertAt)
    {
        var moving = WithChildren(rows.Where(i => i >= 0 && i < Slots.Count).Distinct().OrderBy(i => i).Select(i => Slots[i]).ToList());
        if (moving.Count == 0) return;
        var rest = Slots.Where(s => !moving.Contains(s)).ToList();
        insertAt = Math.Clamp(insertAt, 0, rest.Count);
        if (moving.Any(m => m.IsFolder)) insertAt = FolderBoundary(rest, insertAt);
        Reorder(rest.Take(insertAt).Concat(moving).Concat(rest.Skip(insertAt)).ToList(), moving);
    }

    /// <summary>
    /// A folder with its effects lands only before another folder or at the end: dropped between another folder's effects it would split that
    /// folder, and between the effects outside every folder it would take the ones below it. It goes to the nearest such place.
    /// </summary>
    private static int FolderBoundary(List<FxSlotViewModel> rest, int insertAt) =>
        rest.Select((s, i) => (s, i)).Where(x => x.s.IsFolder).Select(x => x.i).Append(rest.Count)
            .OrderBy(i => Math.Abs(i - insertAt)).ThenBy(i => i).First();

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
            var vm = Track(MakeSlot(slot));
            Slots.Insert(at++, vm);
            added.Add(vm);
        }
        Selected = added[0];
        _mixer.EffectsAdded(_key);
        Changed();
        ReselectRequested?.Invoke(added);
    }

    /// <summary>"Save chain…": the whole chain to <paramref name="path"/>. Null when it worked, else one plain sentence.</summary>
    public string? SaveTo(string path)
    {
        try
        {
            FxChainFile.Write(path, _clipboard.Snapshot(_chain));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Guard.Log("Saving an FX chain", ex);
            return "Couldn't save the chain there.";
        }
    }

    /// <summary>"Load chain…": the chain in <paramref name="path"/> replaces this one. Null when it worked, else one plain sentence and the chain is untouched.</summary>
    public string? LoadFrom(string path)
    {
        var loaded = FxChainFile.Read(path, out var problem);
        if (loaded is null) return problem;
        if (_ties.Devices is null && loaded.Any(s => s.IsFolder)) return "This chain can't hold device folders; they belong to the master.";
        _editors.Leaving(_chain.ToList());
        _reordering = true; // the list is rebuilt below; the editor stays put through it
        foreach (var row in Slots) Untrack(row);
        Slots.Clear();
        _picked.Clear();
        _chain.Clear();
        _chain.AddRange(loaded);
        foreach (var slot in _chain) Slots.Add(Track(MakeSlot(slot)));
        _reordering = false;
        Selected = Slots.FirstOrDefault();
        if (Selected is { } first) ReselectRequested?.Invoke(new[] { first });
        if (_chain.Count > 0) _mixer.EffectsAdded(_key);
        Changed();
        return null;
    }

    /// <summary>Esc: a multi-selection becomes the effect the editor shows. False when there was nothing to reduce.</summary>
    public bool CollapseSelection()
    {
        if (_picked.Count < 2 || Selected is not { } s) return false;
        ReselectRequested?.Invoke(new[] { s });
        return true;
    }
}
