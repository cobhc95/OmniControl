namespace OmniControl.App.Features.Hotkeys.Picker;

/// A card whose action is chosen in <see cref="PickerWindow"/> (a mouse button, a gesture).
public interface IPickerCard
{
    string Label { get; }

    /// A fresh picker on the card's current action; <paramref name="startInSettings"/> opens it on that action's settings.
    PickerModel MenuFor(bool startInSettings);

    /// Takes the action and settings chosen (also called live while settings of the current action change).
    void Pick(PickResult pick);
}
