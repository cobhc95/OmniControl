using OmniControl.App.Audio;
using OmniControl.App.Fx.Presets;

namespace OmniControl.App.Fx;

/// <summary>
/// The chain's part in device folder presets (<see cref="FolderPresets"/>): the preset manager reads the selected folder through
/// <see cref="IFxFolderTarget"/> and applies a preset to it here, so the list and the editor follow at once; "Save folder as preset"
/// hands the folder over through <see cref="FxChainTies.SaveFolderPreset"/>.
/// </summary>
public sealed partial class FxChainViewModel : IFxFolderTarget
{
    private int SelectedIndex => Selected is { } s ? Slots.IndexOf(s) : -1;

    FolderPreset? IFxFolderTarget.Read() =>
        FxFolders.TargetFolder(_chain, SelectedIndex) is var at and >= 0 ? FxFolders.ReadFolder(_chain, at) : null;

    void IFxFolderTarget.Apply(FolderPreset content)
    {
        var (folder, removed) = FxFolders.ApplyFolder(_chain, FxFolders.TargetFolder(_chain, SelectedIndex), content);
        _editors.Leaving(removed);
        _reordering = true; // the list is rebuilt below; the editor stays put through it
        foreach (var gone in Slots.Where(v => removed.Contains(v.Slot)).ToList())
        {
            Untrack(gone);
            Slots.Remove(gone);
        }
        for (var i = 0; i < _chain.Count; i++) // the list in the chain's order, a row made for each new effect
        {
            var row = Slots.FirstOrDefault(v => ReferenceEquals(v.Slot, _chain[i]));
            if (row is null) Slots.Insert(i, Track(MakeSlot(_chain[i])));
            else if (Slots.IndexOf(row) != i) Slots.Move(Slots.IndexOf(row), i);
        }
        _reordering = false;
        var folderRow = Slots.First(v => ReferenceEquals(v.Slot, folder));
        Selected = folderRow;
        _mixer.EffectsAdded(_key);
        Changed();
        folderRow.Folder?.Reload();
        ReselectRequested?.Invoke(new[] { folderRow });
    }

    /// <summary>The folder's "Save folder as preset…": the folder with its effects goes to the preset store and the preset manager opens at it.</summary>
    private void SaveFolderAsPreset(FxSlot folder) => _ties.SaveFolderPreset(FxFolders.ReadFolder(_chain, _chain.IndexOf(folder)));
}
