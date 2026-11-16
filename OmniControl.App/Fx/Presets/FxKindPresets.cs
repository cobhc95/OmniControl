using OmniControl.App.Audio;
using OmniControl.App.Presets;
using OmniControl.App.Services;

namespace OmniControl.App.Fx.Presets;

/// <summary>An effect no FX window shows (the master's while its window is closed): a preset is written into it directly and saved.</summary>
internal sealed class SlotPresetTarget : IFxPresetTarget
{
    private readonly FxKind _kind;
    private readonly FxSlot _slot;
    private readonly UserPresetStore _store;
    private readonly Action _save;

    /// <param name="save">Saves the chain and tells the running engine.</param>
    public SlotPresetTarget(FxKind kind, FxSlot slot, UserPresetStore store, Action save) => (_kind, _slot, _store, _save) = (kind, slot, store, save);

    public PresetContent Read() => PresetCatalog.Read(_kind, _slot);

    public void Apply(PresetContent content)
    {
        PresetCatalog.Write(_kind, _slot, content, _store.Of(_kind.Kind));
        _save();
    }
}

/// <summary>
/// One effect kind's presets (an equaliser's, the compressor's) as a preset group of the preset manager: the built-in ones first (tagged,
/// never renamed, moved or deleted), then the user's own from <see cref="UserPresetStore"/> in their order. The effect they are read
/// from and applied to is the target the FX windows give (<see cref="FxWindowService"/>: the selected effect of this kind in the FX
/// window used last, else the master's first one); without one, nothing is applied and the manager says why. The rules are the old
/// manager's: names from <see cref="PresetRules"/>, a built-in name is reserved, copies are named "name copy", a file's preset is
/// added under the file's name.
/// </summary>
internal sealed class FxKindPresets : IPresetProvider, IDisposable
{
    private const string BuiltInPrefix = "builtin.";
    private const string NewName = "My preset";
    private const string ImportedName = "Imported preset";
    private const string ReservedText = "That name belongs to a built-in preset.";
    private const string TakenText = "A preset of this name exists.";
    private const string ChangeFailed = "The preset could not be changed: ";

    private readonly FxKind _kind;
    private readonly UserPresetStore _store;
    private readonly Func<FxKind, IFxPresetTarget?> _target;

    /// <param name="target">The effect of this kind a preset is read from and applied to now, or null when there is none.</param>
    public FxKindPresets(FxKind kind, UserPresetStore store, Func<FxKind, IFxPresetTarget?> target)
    {
        (_kind, _store, _target) = (kind, store, target);
        Words = new PresetWords("Apply", "Apply preset", "+ Save the current settings as a preset",
            $"Keeps the settings of the {kind.Name} selected in the FX window used last (else the master's) as a new preset");
        store.Changed += OnStoreChanged;
    }

    /// <summary>The preset group's id of an effect kind (also part of its presets' shortcut ids: never rename).</summary>
    public static string GroupId(FxKind kind) => "volume.fx." + kind.Kind;

    public PresetWords Words { get; }

    public PresetSwitch? Switch => null;

    public bool CanDuplicate => true;

    public bool CanExchangeFiles => true;

    public string? AddUnavailableReason => null; // the target is looked for when the button is pressed: an FX window may open meanwhile

    private string NoTarget => $"No {_kind.Name} to use: add one to the master, or select one in an FX window.";

    public IReadOnlyList<PresetEntry> Entries
    {
        get
        {
            var own = _store.Of(_kind.Kind);
            var match = _target(_kind)?.Read() is { } now ? PresetCatalog.Matching(_kind, now, own) : null;
            return PresetCatalog.BuiltIns(_kind)
                .Select(b => new PresetEntry(BuiltInPrefix + b.Name, b.Name)
                {
                    BuiltIn = true, CanRename = false, CanDelete = false, CanMove = false,
                    Tags = new[] { new TagItem("built-in") },
                    InUse = match is { BuiltIn: true } m && m.Name == b.Name,
                })
                .Concat(own.Select(p => new PresetEntry(p.Id!, p.Name) { InUse = match is { BuiltIn: false } m && PresetRules.Same(m.Name, p.Name) }))
                .ToList();
        }
    }

    public PresetEntry? Find(string id) => Entries.FirstOrDefault(e => e.Id == id);

    private UserPreset? Own(string id) => _store.FindById(id) is { } preset && preset.Kind == _kind.Kind ? preset : null;

    private PresetContent? ContentOf(string id) =>
        id.StartsWith(BuiltInPrefix, StringComparison.Ordinal)
            ? PresetCatalog.BuiltIns(_kind).FirstOrDefault(b => b.Name == id[BuiltInPrefix.Length..]).Content
            : Own(id)?.Content;

    private List<string> TakenNames() => PresetCatalog.BuiltIns(_kind).Select(b => b.Name).Concat(_store.Names(_kind.Kind)).ToList();

    public string? Apply(string id)
    {
        if (ContentOf(id) is not { } content) return null;
        if (_target(_kind) is not { } target) return NoTarget;
        target.Apply(content.Copy());
        Changed?.Invoke(); // the preset in use moved
        return null;
    }

    public PresetChange Add()
    {
        if (_target(_kind) is not { } target) return PresetChange.Refused(NoTarget);
        var name = PresetRules.FirstFree(NewName, TakenNames());
        return Save(name, target.Read());
    }

    public PresetChange Rename(string id, string name)
    {
        if (Own(id) is not { } preset) return PresetChange.Done;
        name = name.Trim();
        if (PresetRules.Error(name) is { } problem) return PresetChange.Refused(problem);
        if (PresetCatalog.BuiltIns(_kind).Any(b => PresetRules.Same(b.Name, name))) return PresetChange.Refused(ReservedText);
        if (_store.Of(_kind.Kind).Any(p => p.Id != id && PresetRules.Same(p.Name, name))) return PresetChange.Refused(TakenText);
        return Try(() => _store.Rename(_kind.Kind, preset.Name, name));
    }

    /// <summary>The user's own presets come after the built-in ones, so the place among them is the place in the card less the built-ins.</summary>
    public PresetChange Move(string id, int index)
    {
        if (Own(id) is not { } preset) return PresetChange.Done;
        return Try(() => _store.Move(_kind.Kind, preset.Name, index - PresetCatalog.BuiltIns(_kind).Count));
    }

    public Action? Delete(string id) => Own(id) is { } preset ? _store.Delete(_kind.Kind, new[] { preset.Name }) : null;

    /// <summary>A copy of any preset (a built-in one too: the copy is the user's own to change).</summary>
    public PresetChange Duplicate(string id)
    {
        if (Find(id) is not { } entry || ContentOf(id) is not { } content) return PresetChange.Done;
        return Save(PresetRules.CopyName(entry.Name, TakenNames()), content);
    }

    public byte[]? Export(string id) =>
        Find(id) is { } entry && ContentOf(id) is { } content
            ? UserPresetStore.Export(new UserPreset { Kind = _kind.Kind, Name = entry.Name, Content = content })
            : null;

    /// <summary>Adds a file's preset under the file's name (a copy name when taken); refused when it is no preset of this kind.</summary>
    public PresetChange Import(string fileName, byte[] bytes)
    {
        if (UserPresetStore.Read(bytes) is not { } preset || preset.Kind != _kind.Kind)
            return PresetChange.Refused($"\"{fileName}\" is not a preset of {_kind.Name}.");
        var name = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (PresetRules.Error(name) is not null) name = ImportedName;
        var taken = TakenNames();
        if (taken.Contains(name, StringComparer.OrdinalIgnoreCase)) name = PresetRules.CopyName(name, taken);
        return Save(name, preset.Content);
    }

    private PresetChange Save(string name, PresetContent content)
    {
        string? id = null;
        var result = Try(() => id = _store.Save(_kind.Kind, name, content));
        return id is not null ? PresetChange.Made(id) : result;
    }

    /// <summary>Runs a change; a file problem is said in the manager instead of crashing it.</summary>
    private static PresetChange Try(Action change)
    {
        try
        {
            change();
            return PresetChange.Done;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return PresetChange.Refused(ChangeFailed + ex.Message);
        }
    }

    public event Action? Changed;

    private void OnStoreChanged() => Changed?.Invoke();

    public void Dispose() => _store.Changed -= OnStoreChanged;
}
