namespace OmniControl.App.Shared.Presets;

/// THE base of a preset group (<see cref="IPresetProvider"/>): what most groups leave as it is is said once here (no switch, no copies, no
/// files, adding always possible, a preset found among the entries); a group overrides only what it does differently.
public abstract class PresetProvider : IPresetProvider
{
    public abstract IReadOnlyList<PresetEntry> Entries { get; }

    public abstract PresetWords Words { get; }

    public virtual string? AddUnavailableReason => null;

    public virtual PresetSwitch? Switch => null;

    public virtual bool CanDuplicate => false;

    public virtual bool CanExchangeFiles => false;

    public virtual PresetEntry? Find(string id) => Entries.FirstOrDefault(e => e.Id == id);

    public abstract string? Apply(string id);

    public abstract PresetChange Add();

    public abstract PresetChange Rename(string id, string name);

    public abstract PresetChange Move(string id, int index);

    public abstract Action? Delete(string id);

    public virtual PresetChange Duplicate(string id) => PresetChange.Done;

    public virtual byte[]? Export(string id) => null;

    public virtual PresetChange Import(string fileName, byte[] bytes) => PresetChange.Done;

    public abstract event Action? Changed;
}

/// A preset group over a <see cref="PresetList{T}"/> after <c>builtIns</c> built-in presets that always come first: renames, moves and
/// deletes go to the list by name; the entries are worked out once per change (an "in use" may compare whole settings), so the owner calls
/// <see cref="NotifyChanged"/> whenever the live settings change.
public abstract class ListPresetProvider<T> : PresetProvider where T : class, INamedPreset
{
    private readonly int _builtIns;
    private IReadOnlyList<PresetEntry>? _entries;

    protected ListPresetProvider(PresetList<T> list, int builtIns)
    {
        (List, _builtIns) = (list, builtIns);
        list.Changed += NotifyChanged;
    }

    protected PresetList<T> List { get; }

    public sealed override IReadOnlyList<PresetEntry> Entries => _entries ??= MakeEntries();

    protected abstract IReadOnlyList<PresetEntry> MakeEntries();

    public override PresetChange Rename(string id, string name) =>
        NameOf(id) is { } from && List.Rename(from, name) is { } problem ? PresetChange.Refused(problem) : PresetChange.Done;

    public override PresetChange Move(string id, int index)
    {
        if (NameOf(id) is { } name) List.Move(name, index - _builtIns);
        return PresetChange.Done;
    }

    public override Action? Delete(string id) => NameOf(id) is { } name ? List.Delete(name) : null;

    private string? NameOf(string id) => List.Items.FirstOrDefault(p => p.Id == id)?.Name;

    public override event Action? Changed;

    protected void NotifyChanged()
    {
        _entries = null;
        Changed?.Invoke();
    }
}
