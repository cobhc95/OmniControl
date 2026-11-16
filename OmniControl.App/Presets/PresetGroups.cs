namespace OmniControl.App.Presets;

/// <summary>
/// One group of presets in the master list: <see cref="Section"/> is the title of the page it belongs to (the preset manager's left
/// pane), <see cref="Title"/> the group's name on that page (the card's title: "Mouse Buttons", "Hardware", an effect's name).
/// </summary>
public sealed record PresetGroup(string Id, string Section, string Title, IPresetProvider Provider)
{
    /// <summary>Applying a preset of this group moves a mouse or trackpad layer: the Mouse picker has its own layer actions, so it does not list the preset's shortcut action.</summary>
    public bool MovesLayer { get; init; }
}

/// <summary>
/// The master list of preset groups (AGENTS.md "One master list"): each group registers once, where its presets live, and the preset
/// manager and the preset shortcuts read this list and follow it. Adding a group is one <see cref="Register"/> call. Disposing it lets
/// go of the providers that hold subscriptions.
/// </summary>
public sealed class PresetGroups : IDisposable
{
    /// <summary>The Shortcuts page's subgroup of every preset's action ("Displays - Hardware - Presets").</summary>
    public const string Subgroup = "Presets";

    private readonly List<PresetGroup> _all = new();

    /// <summary>Every group, in the order registered (the pages' order, then each page's own order).</summary>
    public IReadOnlyList<PresetGroup> All => _all;

    /// <summary>A group was added (after the ones already there).</summary>
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

    public PresetGroup? Find(string id) => _all.FirstOrDefault(g => g.Id == id);

    /// <summary>The groups of one section (page title), in their order.</summary>
    public IReadOnlyList<PresetGroup> In(string section) => _all.Where(g => g.Section == section).ToList();

    public void Dispose()
    {
        foreach (var provider in _all.Select(g => g.Provider).OfType<IDisposable>()) provider.Dispose();
    }
}
