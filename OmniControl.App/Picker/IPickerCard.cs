namespace OmniControl.App.Picker;

/// <summary>A card whose action is chosen in <see cref="PickerWindow"/> (a mouse button, a gesture).</summary>
public interface IPickerCard
{
    string Label { get; }

    /// <summary>A fresh picker on the card's current action; <paramref name="startInSettings"/> opens it on that action's settings.</summary>
    PickerModel MenuFor(bool startInSettings);

    /// <summary>Takes the action and settings chosen (also called live while settings of the current action change).</summary>
    void Pick(PickResult pick);
}
