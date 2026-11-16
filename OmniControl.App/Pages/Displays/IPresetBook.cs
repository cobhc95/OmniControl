using OmniControl.App.Display;

namespace OmniControl.App.Pages.Displays;

/// <summary>One part of a preset the manager lists under its name (a monitor) and whether it is connected now.</summary>
public sealed record PresetPart(string Name, bool Connected);

/// <summary>The words of the preset manager's card that differ between kinds of preset: the add button, its tooltip, a new preset's first name.</summary>
public sealed record PresetBookText(string AddText, string AddTip, string NewName);

/// <summary>
/// One kind of preset the Displays page offers (picture presets, monitor presets), as the header drop-down and the preset manager see
/// it: the saved presets, which one the live settings show, and what apply, save, rename, move and delete do. Both are made from this
/// one shape, so there is one drop-down and one manager card (<see cref="BookPresets"/>) for every kind.
/// </summary>
public interface IPresetBook
{
    /// <summary>What search results call the drop-down.</summary>
    string Label { get; }

    PresetBookText Text { get; }

    /// <summary>The saved presets in their order.</summary>
    IReadOnlyList<INamedPreset> Presets { get; }

    /// <summary>The saved presets' names in their order.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>The preset the live settings show (the first that matches); null when none does.</summary>
    string? Shown { get; }

    /// <summary>Why no preset can be saved now (shown in the greyed drop-down); null when one can.</summary>
    string? UnavailableReason { get; }

    void Apply(string name);

    /// <summary>Keeps what is shown now under the name (a name that exists is replaced).</summary>
    void SaveCurrent(string name);

    /// <summary>Returns why it cannot be renamed, or null when done.</summary>
    string? Rename(string from, string to);

    /// <summary>Puts the preset at this place in the list.</summary>
    void Move(string name, int index);

    /// <summary>Deletes the preset; the result puts it back (null when there was nothing to delete).</summary>
    Action? Delete(string name);

    /// <summary>What the preset holds, for the manager to list (empty when it has no parts worth listing).</summary>
    IReadOnlyList<PresetPart> PartsOf(string name);

    /// <summary>The saved list, or what the live settings show, changed.</summary>
    event Action? Changed;
}
