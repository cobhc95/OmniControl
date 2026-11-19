using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Volume.Fx.Presets;

/// The master's device folder a folder preset is read from and applied to (the FX window's, or the saved chain when no window is open).
internal interface IFxFolderTarget
{
    /// The folder as a preset; null when the master has no folder.
    FolderPreset? Read();

    /// Replaces the folder's effects and Mix (a folder for the preset's device is added when the master has none).
    void Apply(FolderPreset content);
}

/// The master's folder when no FX window shows it: the preset is read from and written into the stored chain directly and saved.
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

/// The device folders saved as presets, as a preset group of the preset manager ("Save folder as
/// preset"): each holds a folder's device, Mix and effects with their values (<see cref="FolderPreset"/>, kept by
/// <see cref="UserPresetStore"/>). Applying one replaces the effects and Mix of the selected folder of the master (else its first folder;
/// with no folder, one is made for the preset's device). Names follow <see cref="PresetRules"/>; there are no built-in folder presets
/// and no files.
internal sealed class FolderPresets : PresetProvider, IDisposable
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

    /// The preset group's id (also part of its presets' shortcut ids: never rename).
    public const string GroupId = "volume.fx.folder";

    public override PresetWords Words { get; }

    public override bool CanDuplicate => true;

    public override IReadOnlyList<PresetEntry> Entries
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

    private List<string> TakenNames() => _store.Folders.Select(f => f.Name).ToList();

    /// Keeps the folder as a new preset under a first name of its own; the id of the preset.
    public string Save(FolderPreset content) => _store.SaveFolder(PresetRules.FirstFree(NewName, TakenNames()), content);

    public override string? Apply(string id)
    {
        if (_store.FindFolder(id) is not { } preset) return null;
        _target().Apply(preset);
        Changed?.Invoke(); // the preset in use moved
        return null;
    }

    public override PresetChange Add()
    {
        if (_target().Read() is not { } now) return PresetChange.Refused(NoFolder);
        return PresetChange.Made(Save(now));
    }

    public override PresetChange Rename(string id, string name)
    {
        name = name.Trim();
        if (PresetRules.Error(name) is { } problem) return PresetChange.Refused(problem);
        if (_store.Folders.Any(f => f.Id != id && PresetRules.Same(f.Name, name))) return PresetChange.Refused(TakenText);
        _store.RenameFolder(id, name);
        return PresetChange.Done;
    }

    public override PresetChange Move(string id, int index)
    {
        _store.MoveFolder(id, index);
        return PresetChange.Done;
    }

    public override Action? Delete(string id) => _store.DeleteFolder(id);

    public override PresetChange Duplicate(string id)
    {
        if (_store.FindFolder(id) is not { } preset) return PresetChange.Done;
        return PresetChange.Made(_store.SaveFolder(PresetRules.CopyName(preset.Name, TakenNames()), preset));
    }

    public override PresetChange Import(string fileName, byte[] bytes) => PresetChange.Refused("Device folder presets are not kept in files.");

    public override event Action? Changed;

    private void OnStoreChanged() => Changed?.Invoke();

    public void Dispose() => _store.Changed -= OnStoreChanged;
}
