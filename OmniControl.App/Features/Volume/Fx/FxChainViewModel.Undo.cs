using System.Text.Json;
using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Fx;

/// The chain's undo and redo: after every saved change the chain is compared with how it was, and the difference becomes one named
/// step of <see cref="History"/>; undoing puts the earlier chain back through the same rebuild, save and engine path as any edit.
public sealed partial class FxChainViewModel
{
    /// The chain at one moment: its effects (the same objects, so the engine keeps them) and each one's settings as text. An effect's
    /// unchanged text is shared with the moment before, so a step costs only what changed.
    private sealed record ChainState(FxSlot[] Slots, string[] Texts);

    private ChainState _now = new(Array.Empty<FxSlot>(), Array.Empty<string>());
    private string? _nextName;

    /// This window's undo history (its Edit menu); cleared when the window closes.
    public UndoHistory History { get; } = new();

    private ChainState Capture()
    {
        var slots = _chain.ToArray();
        var texts = new string[slots.Length];
        for (var i = 0; i < slots.Length; i++)
        {
            var text = JsonSerializer.Serialize(slots[i]);
            var was = Array.IndexOf(_now.Slots, slots[i]);
            texts[i] = was >= 0 && _now.Texts[was] == text ? _now.Texts[was] : text;
        }
        return new ChainState(slots, texts);
    }

    /// Called after every saved change: the difference from the last moment becomes a step (none while a step is being undone or redone).
    private void RecordStep()
    {
        var before = _now;
        var after = _now = Capture();
        var name = _nextName;
        _nextName = null;
        var (described, key) = Describe(before, after);
        if (described is null) return;
        History.Record(name ?? described, () => Restore(before), () => Restore(after), name is null ? key : null);
    }

    /// Puts the chain as it was at <paramref name="state"/>: effects that left come back as themselves, every effect takes its settings
    /// from then, and the list, the editor and the engine follow.
    private void Restore(ChainState state)
    {
        var rows = Slots.ToDictionary(v => v.Slot);
        var leaving = Slots.Where(v => !state.Slots.Contains(v.Slot)).ToList();
        var selected = Selected;
        _editors.Leaving(leaving.Select(v => v.Slot));
        _reordering = true;
        foreach (var row in leaving)
        {
            Untrack(row);
            Slots.Remove(row);
        }
        _chain.Clear();
        var added = false;
        for (var i = 0; i < state.Slots.Length; i++)
        {
            var slot = state.Slots[i];
            var changed = JsonSerializer.Serialize(slot) != state.Texts[i];
            if (changed) slot.Assign(JsonSerializer.Deserialize<FxSlot>(state.Texts[i])!);
            _chain.Add(slot);
            if (!rows.TryGetValue(slot, out var row))
            {
                row = Track(MakeSlot(slot));
                added = true;
            }
            else if (changed)
            {
                row.Reload();
                row.Folder?.Refresh();
            }
            var at = Slots.IndexOf(row);
            if (at < 0) Slots.Insert(i, row);
            else if (at != i) Slots.Move(at, i);
        }
        _picked.Clear();
        _reordering = false;
        Selected = selected is not null && Slots.Contains(selected) ? selected : Slots.FirstOrDefault();
        if (Selected is { } keep) ReselectRequested?.Invoke(new[] { keep });
        RefreshEditor();
        if (added) _mixer.EffectsAdded(_key);
        Changed();
    }

    /// The step's name ("Remove Compressor", "Change Gain") and, for a change that a drag or typing repeats, what joins it with the next
    /// (one setting of one effect). Null when nothing changed.
    private static (string? Name, object? Key) Describe(ChainState before, ChainState after)
    {
        var added = after.Slots.Except(before.Slots).ToList();
        var removed = before.Slots.Except(after.Slots).ToList();
        if (added.Count > 0) return ((removed.Count > 0 ? "Replace " : "Add ") + Label(added), null);
        if (removed.Count > 0) return ("Remove " + Label(removed), null);
        if (!before.Slots.SequenceEqual(after.Slots))
            return ("Move " + Label(after.Slots.Where((s, i) => Array.IndexOf(before.Slots, s) != i).Take(1).ToList()), null);
        var changed = Enumerable.Range(0, after.Slots.Length).Where(i => !ReferenceEquals(before.Texts[i], after.Texts[i])).ToList();
        if (changed.Count == 0) return (null, null);
        var pairs = changed.Select(i => (Slot: after.Slots[i], Was: Read(before.Texts[i]), Is: Read(after.Texts[i]))).ToList();
        if (pairs.All(p => p.Was.Enabled != p.Is.Enabled))
            return ($"Turn {Label(pairs.Select(p => p.Slot).ToList())} {(pairs[0].Is.Enabled ? "on" : "off")}", null);
        if (pairs.Count > 1) return ($"Change {pairs.Count} effects", null);
        var (slot, was, now) = pairs[0];
        var name = Label(new List<FxSlot> { slot });
        if (was.Wet != now.Wet) return ($"Change {name} Wet", (slot, "wet"));
        var values = Enumerable.Range(0, Math.Min(was.Values.Count, now.Values.Count)).Where(i => was.Values[i] != now.Values[i]).ToList();
        var bands = Differing(was.Bands, now.Bands);
        if (values.Count > 1 || bands > 1 || Text(was.Custom) != Text(now.Custom)) return ($"Apply preset to {name}", null);
        if (values.Count == 1 && FxCatalog.KindOf(slot) is { } kind && values[0] < kind.Params.Count)
            return ($"Change {kind.Params[values[0]].Name}", (slot, values[0]));
        if (bands == 1) return ($"Change {name} bands", (slot, "bands"));
        if (was.Path != now.Path) return ($"Change {name} file", null);
        if (was.DeviceId != now.DeviceId) return ("Change folder device", null);
        return ($"Change {name}", (slot, "other"));
    }

    private static FxSlot Read(string text) => JsonSerializer.Deserialize<FxSlot>(text)!;

    private static string Text<T>(T value) => JsonSerializer.Serialize(value);

    /// How many bands differ (a changed number of bands counts as one: a band added or removed).
    private static int Differing(List<EqBand>? was, List<EqBand>? now) =>
        (was?.Count ?? 0) != (now?.Count ?? 0) ? 1
        : was is null || now is null ? 0
        : was.Zip(now).Count(p => Text(p.First) != Text(p.Second));

    private static string Label(IReadOnlyList<FxSlot> slots) =>
        slots.Count != 1 ? $"{slots.Count} effects" : slots[0].IsFolder ? "device folder" : slots[0].Name;
}
