using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Displays.Pages;

/// One part of a preset the manager lists under its name (a monitor) and whether it is connected now.
public sealed record PresetPart(string Name, bool Connected);

/// The words of the preset manager's card that differ between kinds of preset: the add button, its tooltip, a new preset's first name.
public sealed record PresetBookText(string AddText, string AddTip, string NewName);

/// One kind of preset the Displays page offers (picture presets, monitor presets), as the header drop-down and the preset manager see
/// it: the saved presets, which one the live settings show, and what apply, save, rename, move and delete do. Both are made from this
/// one shape, so there is one drop-down and one manager card (<see cref="BookPresets"/>) for every kind.
public interface IPresetBook
{
    /// What search results call the drop-down.
    string Label { get; }

    PresetBookText Text { get; }

    /// The saved presets in their order.
    IReadOnlyList<INamedPreset> Presets { get; }

    /// The saved presets' names in their order.
    IReadOnlyList<string> Names { get; }

    /// The preset the live settings show (the first that matches); null when none does.
    string? Shown { get; }

    /// Why no preset can be saved now (shown in the greyed drop-down); null when one can.
    string? UnavailableReason { get; }

    void Apply(string name);

    /// Keeps what is shown now under the name (a name that exists is replaced).
    void SaveCurrent(string name);

    /// Returns why it cannot be renamed, or null when done.
    string? Rename(string from, string to);

    /// Puts the preset at this place in the list.
    void Move(string name, int index);

    /// Deletes the preset; the result puts it back (null when there was nothing to delete).
    Action? Delete(string name);

    /// What the preset holds, for the manager to list (empty when it has no parts worth listing).
    IReadOnlyList<PresetPart> PartsOf(string name);

    /// The saved list, or what the live settings show, changed.
    event Action? Changed;
}
