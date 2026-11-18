namespace OmniControl.App.Audio;

/// <summary>
/// Device folders in the master's chain (docs/mockups/fx-device-folders.html). A folder is a slot of <see cref="Kind"/> holding a device id;
/// the effects after it, up to the next folder, are inside it, and the effects before the first folder run for every device. The chain
/// stays one flat list in mixer.json, so a chain saved before folders existed (no folder slot) loads and runs exactly as it did.
/// The one place that knows which effects run on which device.
/// </summary>
public static class FxFolders
{
    /// <summary>The stored kind of a folder slot; never renamed.</summary>
    public const string Kind = "folder";

    public static bool HasFolders(IReadOnlyList<FxSlot> chain) => chain.Any(s => s.IsFolder);

    /// <summary>Index of the folder the slot at <paramref name="index"/> is in (or is); -1 for an effect before the first folder.</summary>
    public static int FolderIndexOf(IReadOnlyList<FxSlot> chain, int index)
    {
        for (var i = Math.Min(index, chain.Count - 1); i >= 0; i--) if (chain[i].IsFolder) return i;
        return -1;
    }

    /// <summary>The slots a folder row stands for: itself and its effects (the rows that move or go with it).</summary>
    public static (int Start, int Count) BlockOf(IReadOnlyList<FxSlot> chain, int folderIndex)
    {
        var end = folderIndex + 1;
        while (end < chain.Count && !chain[end].IsFolder) end++;
        return (folderIndex, end - folderIndex);
    }

    /// <summary>Where an effect added to no folder goes: the end of the effects that run for every device.</summary>
    public static int TopLevelEnd(IReadOnlyList<FxSlot> chain)
    {
        var first = chain.ToList().FindIndex(s => s.IsFolder);
        return first < 0 ? chain.Count : first;
    }

    /// <summary>Where an effect added to the folder at <paramref name="folderIndex"/> goes: after its last effect.</summary>
    public static int FolderEnd(IReadOnlyList<FxSlot> chain, int folderIndex) => BlockOf(chain, folderIndex) is var (s, n) ? s + n : chain.Count;

    /// <summary>
    /// What runs while <paramref name="deviceId"/> plays, in order, as parts: the effects before the first folder (no folder), then each
    /// enabled folder for that device with its effects (so an engine can mix that folder's result with its dry sound, the folder's Mix).
    /// A null or unknown device gets only the first part.
    /// </summary>
    public static List<FxPart> PartsFor(IReadOnlyList<FxSlot> chain, string? deviceId)
    {
        var parts = new List<FxPart>();
        var current = new List<FxSlot>();
        FxSlot? folder = null;
        var runs = true;
        void Close()
        {
            if (runs) parts.Add(new FxPart(folder, current));
            current = new List<FxSlot>();
        }
        foreach (var slot in chain)
        {
            if (slot.IsFolder)
            {
                Close();
                folder = slot;
                runs = slot.Enabled && deviceId is not null && string.Equals(slot.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase);
            }
            else current.Add(slot);
        }
        Close();
        return parts;
    }

    /// <summary>The folder a folder preset reads from or applies to: the one the selected slot is in (or is), else the first folder; -1 when there is none.</summary>
    public static int TargetFolder(IReadOnlyList<FxSlot> chain, int selectedIndex)
    {
        var at = selectedIndex >= 0 ? FolderIndexOf(chain, selectedIndex) : -1;
        return at >= 0 ? at : chain.ToList().FindIndex(s => s.IsFolder);
    }

    /// <summary>The folder at <paramref name="folderIndex"/> as a preset (copies of its effects; no name or id yet).</summary>
    public static FolderPreset ReadFolder(IReadOnlyList<FxSlot> chain, int folderIndex)
    {
        var folder = chain[folderIndex];
        var (start, count) = BlockOf(chain, folderIndex);
        return new FolderPreset
        {
            DeviceId = folder.DeviceId, DeviceName = folder.Name, Mix = folder.Wet,
            Effects = chain.Skip(start + 1).Take(count - 1).Select(e => e.Copy()).ToList(),
        };
    }

    /// <summary>
    /// Puts a folder preset into the chain: the effects of the folder at <paramref name="folderIndex"/> are replaced by copies of the
    /// preset's and it takes the preset's Mix (it keeps its own device); with no folder (-1) a folder for the preset's device is added at
    /// the end. Returns the folder's slot and the effects that were taken out (for their editors to close).
    /// </summary>
    public static (FxSlot Folder, List<FxSlot> Removed) ApplyFolder(List<FxSlot> chain, int folderIndex, FolderPreset preset)
    {
        var removed = new List<FxSlot>();
        FxSlot folder;
        if (folderIndex < 0)
        {
            folder = new FxSlot { Kind = Kind, Name = preset.DeviceName, DeviceId = preset.DeviceId };
            chain.Add(folder);
            folderIndex = chain.Count - 1;
        }
        else
        {
            folder = chain[folderIndex];
            var (start, count) = BlockOf(chain, folderIndex);
            removed.AddRange(chain.GetRange(start + 1, count - 1));
            chain.RemoveRange(start + 1, count - 1);
        }
        folder.Wet = preset.Mix;
        chain.InsertRange(folderIndex + 1, preset.Effects.Select(e => e.Copy()));
        return (folder, removed);
    }

    /// <summary>The effects that run while <paramref name="deviceId"/> plays, in order (no folder slot is in the result).</summary>
    public static List<FxSlot> ChainFor(IReadOnlyList<FxSlot> chain, string? deviceId) =>
        PartsFor(chain, deviceId).SelectMany(p => p.Effects).ToList();
}

/// <summary>Effects that run together: a device folder's (with the folder, for its Mix) or the ones outside every folder (no folder).</summary>
public sealed record FxPart(FxSlot? Folder, IReadOnlyList<FxSlot> Effects);
