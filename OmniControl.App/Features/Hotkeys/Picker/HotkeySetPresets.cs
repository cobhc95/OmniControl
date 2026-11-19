using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// The shortcut sets (<see cref="HotkeySets"/>) as a preset group of the preset manager: the sets in their order, the one in use marked,
/// applying one switches to it, adding keeps the bindings in force under a first free name. The last set cannot be deleted.
internal sealed class HotkeySetPresets : PresetProvider
{
    private readonly HotkeySets _sets;

    public HotkeySetPresets(HotkeySets sets) => _sets = sets;

    public override PresetWords Words { get; } = new("Switch to", "Use shortcut set", "+ Save the current shortcuts as a set",
        "Keeps every shortcut and device choice now in force as a new set");

    public override IReadOnlyList<PresetEntry> Entries => _sets.Sets.Select(Entry).ToList();

    private PresetEntry Entry(HotkeySet set) => new(set.Id, set.Name) { InUse = set.Id == _sets.CurrentId, CanDelete = _sets.Sets.Count > 1 };

    public override PresetEntry? Find(string id) => _sets.Find(id) is { } set ? Entry(set) : null;

    public override string? Apply(string id)
    {
        _sets.Switch(id);
        return null;
    }

    public override PresetChange Add() => PresetChange.Made(_sets.Add(PresetRules.FirstFree(HotkeySets.NewName, Names)).Id);

    public override PresetChange Rename(string id, string name)
    {
        var error = PresetRules.Error(name);
        if (error is null && _sets.Sets.Any(s => s.Id != id && PresetRules.Same(s.Name, name.Trim()))) error = "A set with this name exists.";
        if (error is not null) return PresetChange.Refused(error);
        _sets.Rename(id, name.Trim());
        return PresetChange.Done;
    }

    public override PresetChange Move(string id, int index)
    {
        _sets.Move(id, index);
        return PresetChange.Done;
    }

    public override Action? Delete(string id) => _sets.Delete(id);

    private List<string> Names => _sets.Sets.Select(s => s.Name).ToList();

    public override event Action? Changed
    {
        add => _sets.Changed += value;
        remove => _sets.Changed -= value;
    }
}
