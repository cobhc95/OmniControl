using OmniControl.App.Audio;
using OmniControl.App.Presets;
using OmniControl.App.Services;

namespace OmniControl.App.Fx.Presets;

/// <summary>The master's device folder a folder preset is read from and applied to (the FX window's, or the saved chain when no window is open).</summary>
internal interface IFxFolderTarget
{
    /// <summary>The folder as a preset; null when the master has no folder.</summary>
    FolderPreset? Read();

    /// <summary>Replaces the folder's effects and Mix (a folder for the preset's device is added when the master has none).</summary>
    void Apply(FolderPreset content);
}

/// <summary>The master's folder when no FX window shows it: the preset is read from and written into the stored chain directly and saved.</summary>
internal sealed class SlotFolderTarget : IFxFolderTarget
{
    private readonly List<FxSlot> _chain;
    private readonly Action _save;

    /// <param name="save">Saves the chain and tells the running engine.</param>
    public SlotFolderTarget(List<FxSlot> chain, Action save) => (_chain, _save) = (chain, save);

    public FolderPreset? Read() => FxFolders.TargetFolder(_chain, -1) is var at and >= 0 ? FxFolders.ReadFolder(_chain, at) : null;

    public void Apply(FolderPreset content)
    {
        FxFolders.ApplyFolder(_chain, FxFolders.TargetFolder(_chain, -1), content);
        _save();
    }
}

/// <summary>
/// The device folders saved as presets, as a preset group of the preset manager (docs/mockups/fx-device-folders.html, "Save folder as
/// preset"): each holds a folder's device, Mix and effects with their values (<see cref="FolderPreset"/>, kept by
/// <see cref="UserPresetStore"/>). Applying one replaces the effects and Mix of the selected folder of the master (else its first folder;
/// with no folder, one is made for the preset's device). Names follow <see cref="PresetRules"/>; there are no built-in folder presets
/// and no files.
/// </summary>
internal sealed class FolderPresets : IPresetProvider, IDisposable
{
    private const string NewName = "My folder";
    private const string TakenText = "A preset of this name exists.";
    private const string NoFolder = "No device folder to use: add one to the master's effects.";

    private readonly UserPresetStore _store;
    private readonly Func<IFxFolderTarget> _target;

    /// <param name="target">The master's folder a preset is read from and applied to now.</param>
    public FolderPresets(UserPresetStore store, Func<IFxFolderTarget> target)
    {
        (_store, _target) = (store, target);
        Words = new PresetWords("Apply", "Apply folder preset", "+ Save the selected folder as a preset",
            "Keeps the device, Mix and effects of the master's selected device folder (else its first) as a new preset");
        store.Changed += OnStoreChanged;
    }

    /// <summary>The preset group's id (also part of its presets' shortcut ids: never rename).</summary>
    public const string GroupId = "volume.fx.folder";

    public PresetWords Words { get; }

    public PresetSwitch? Switch => null;

    public bool CanDuplicate => true;

    public bool CanExchangeFiles => false;

    public string? AddUnavailableReason => null; // the folder is looked for when the button is pressed

    public IReadOnlyList<PresetEntry> Entries
    {
        get
        {
            var now = _target().Read();
            return _store.Folders.Select(f => new PresetEntry(f.Id!, f.Name)
            {
                Tags = new[] { new TagItem(f.DeviceName) },
                InUse = f.Matches(now),
            }).ToList();
        }
    }

    public PresetEntry? Find(string id) => Entries.FirstOrDefault(e => e.Id == id);

    private List<string> TakenNames() => _store.Folders.Select(f => f.Name).ToList();

    /// <summary>Keeps the folder as a new preset under a first name of its own; the id of the preset.</summary>
    public string Save(FolderPreset content) => _store.SaveFolder(PresetRules.FirstFree(NewName, TakenNames()), content);

    public string? Apply(string id)
    {
        if (_store.FindFolder(id) is not { } preset) return null;
        _target().Apply(preset);
        Changed?.Invoke(); // the preset in use moved
        return null;
    }

    public PresetChange Add()
    {
        if (_target().Read() is not { } now) return PresetChange.Refused(NoFolder);
        return PresetChange.Made(Save(now));
    }

    public PresetChange Rename(string id, string name)
    {
        name = name.Trim();
        if (PresetRules.Error(name) is { } problem) return PresetChange.Refused(problem);
        if (_store.Folders.Any(f => f.Id != id && PresetRules.Same(f.Name, name))) return PresetChange.Refused(TakenText);
        _store.RenameFolder(id, name);
        return PresetChange.Done;
    }

    public PresetChange Move(string id, int index)
    {
        _store.MoveFolder(id, index);
        return PresetChange.Done;
    }

    public Action? Delete(string id) => _store.DeleteFolder(id);

    public PresetChange Duplicate(string id)
    {
        if (_store.FindFolder(id) is not { } preset) return PresetChange.Done;
        return PresetChange.Made(_store.SaveFolder(PresetRules.CopyName(preset.Name, TakenNames()), preset));
    }

    public byte[]? Export(string id) => null;

    public PresetChange Import(string fileName, byte[] bytes) => PresetChange.Refused("Device folder presets are not kept in files.");

    public event Action? Changed;

    private void OnStoreChanged() => Changed?.Invoke();

    public void Dispose() => _store.Changed -= OnStoreChanged;
}
