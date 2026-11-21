namespace OmniControl.App.Shared.Presets;

/// One group of presets in the master list: <see cref="Section"/> is the title of the page it belongs to (the preset manager's left
/// pane), <see cref="Title"/> the group's name on that page (the card's title: "Mouse Buttons", "Hardware", an effect's name).
public sealed record PresetGroup(string Id, string Section, string Title, IPresetProvider Provider)
{
    /// Applying a preset of this group moves a mouse or trackpad layer: the Mouse picker has its own layer actions, so it does not list the preset's shortcut action.
    public bool MovesLayer { get; init; }
}

/// The master list of preset groups: each group registers once, where its presets live, and the preset
/// manager and the preset shortcuts read this list and follow it. Adding a group is one <see cref="Register"/> call. Disposing it lets
/// go of the providers that hold subscriptions.
public sealed class PresetGroups : IPresetGroups, IDisposable
{
    /// The Shortcuts page's subgroup of every preset's action ("Displays - Hardware - Presets").
    public const string Subgroup = "Presets";

    private readonly List<PresetGroup> _all = new();

    /// Every group, in the order registered (the pages' order, then each page's own order).
    public IReadOnlyList<PresetGroup> All => _all;

    /// A group was added (after the ones already there).
    public event Action<PresetGroup>? Registered;

    public void Register(PresetGroup group)
    {
        if (_all.Any(g => g.Id == group.Id)) throw new InvalidOperationException($"The preset group {group.Id} is registered twice.");
        _all.Add(group);
        Registered?.Invoke(group);
    }

    public void Register(IEnumerable<PresetGroup> groups)
    {
        foreach (var group in groups) Register(group);
    }

    /// A group was taken out (its feature was switched off).
    public event Action<PresetGroup>? Removed;

    /// Takes the group out; its presets stay saved by their owner.
    public void Unregister(string id)
    {
        if (Find(id) is not { } group) return;
        _all.Remove(group);
        Removed?.Invoke(group);
        (group.Provider as IDisposable)?.Dispose();
    }

    public PresetGroup? Find(string id) => _all.FirstOrDefault(g => g.Id == id);

    /// The groups of one section (page title), in their order.
    public IReadOnlyList<PresetGroup> In(string section) => _all.Where(g => g.Section == section).ToList();

    public void Dispose()
    {
        foreach (var provider in _all.Select(g => g.Provider).OfType<IDisposable>()) provider.Dispose();
    }
}
