using OmniControl.App.Services;

namespace OmniControl.App.Presets;

/// <summary>
/// One preset as the preset manager lists it. <see cref="Id"/> never changes (a shortcut and its action follow it through renames and
/// moves); what can be done with it is said here, so the manager has no per-kind code.
/// </summary>
public sealed record PresetEntry(string Id, string Name)
{
    /// <summary>The live settings are this preset (the layer in force, the picture shown...).</summary>
    public bool InUse { get; init; }

    /// <summary>Comes with the app: it can be applied, copied and given a shortcut, never renamed, moved or deleted.</summary>
    public bool BuiltIn { get; init; }

    /// <summary>Small facts shown beside the preset's name in the preset manager (a monitor it holds; "built-in").</summary>
    public IReadOnlyList<TagItem> Tags { get; init; } = Array.Empty<TagItem>();

    public bool CanRename { get; init; } = true;
    public bool CanDelete { get; init; } = true;
    public bool CanMove { get; init; } = true;

    /// <summary>Delete only empties it (a layer that is always listed): the manager says Clear instead of Delete.</summary>
    public bool ClearsOnly { get; init; }
}

/// <summary>What a change of the preset manager came to: why it was refused, or the id of the preset it made (null when it made none).</summary>
public sealed record PresetChange(string? Problem = null, string? Id = null)
{
    public static readonly PresetChange Done = new();

    public static PresetChange Refused(string problem) => new(problem);

    public static PresetChange Made(string id) => new(null, id);
}

/// <summary>The words of a group's card that differ between kinds of preset.</summary>
/// <param name="ApplyText">The row's apply button ("Apply", "Switch to").</param>
/// <param name="ActionVerb">The start of the shortcut action's title ("Apply preset", "Switch to layer").</param>
/// <param name="AddText">The dashed add button at the card's foot ("+ Save the current picture as a preset").</param>
/// <param name="AddTip">Its tooltip: what is kept.</param>
public sealed record PresetWords(string ApplyText, string ActionVerb, string AddText, string AddTip);

/// <summary>An on/off choice that belongs to a whole group (the layers' "Only named or used layers"), shown at its card's foot.</summary>
public sealed record PresetSwitch(string Label, string Tip, Func<bool> Get, Action<bool> Set);

/// <summary>
/// One group of presets as the preset manager sees it, whatever the presets are (mouse or trackpad layers, picture or monitor presets,
/// an effect kind's presets). Each existing preset model is adapted to this shape where it lives; the manager knows nothing else.
/// Every change is applied at once and saved by the model; <see cref="Changed"/> follows. Ids are <see cref="PresetEntry.Id"/>.
/// </summary>
public interface IPresetProvider
{
    /// <summary>The presets in their order, as the card lists them.</summary>
    IReadOnlyList<PresetEntry> Entries { get; }

    /// <summary>The preset with this id, also one the card does not list now (a layer the list leaves out); null when there is none.</summary>
    PresetEntry? Find(string id);

    PresetWords Words { get; }

    /// <summary>Why nothing can be added now (shown on the greyed add button); null when something can.</summary>
    string? AddUnavailableReason { get; }

    /// <summary>The group's own on/off choice, or null.</summary>
    PresetSwitch? Switch { get; }

    bool CanDuplicate { get; }

    /// <summary>Presets can be written to and read from files (the effects' presets).</summary>
    bool CanExchangeFiles { get; }

    /// <summary>Applies the preset (switches to the layer). The problem when it could not be.</summary>
    string? Apply(string id);

    /// <summary>Adds a preset under a first name of its own (the live settings, or the next free layer); the manager then lets the user name it.</summary>
    PresetChange Add();

    PresetChange Rename(string id, string name);

    /// <summary>Puts the preset at this place among <see cref="Entries"/>.</summary>
    PresetChange Move(string id, int index);

    /// <summary>Deletes (or clears) the preset; the result undoes it, null when nothing changed.</summary>
    Action? Delete(string id);

    PresetChange Duplicate(string id);

    /// <summary>The preset as the bytes of a file (null when it cannot be written).</summary>
    byte[]? Export(string id);

    /// <summary>Adds the preset a file holds, under the file's name.</summary>
    PresetChange Import(string fileName, byte[] bytes);

    event Action? Changed;
}
