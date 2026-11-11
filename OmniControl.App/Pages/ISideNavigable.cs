namespace OmniControl.App.Pages;

/// <summary>
/// A card view-model with a sideways choice of its own (the equaliser's bands). While one of its parts is focused,
/// Left/Right call <see cref="Step"/> and Up/Down walk the card's parts (see <c>RowEditing.TryNavigateInside</c>).
/// </summary>
public interface ISideNavigable
{
    /// <summary>Moves the sideways choice by one in <paramref name="direction"/> (-1 or +1).</summary>
    void Step(int direction);
}
