namespace OmniControl.App.Shared.Ui.Pages;

/// A card view-model with a sideways choice of its own (the equaliser's bands). While one of its parts is focused,
/// Left/Right call <see cref="Step"/> and Up/Down walk the card's parts (see <c>RowEditing.TryNavigateInside</c>).
public interface ISideNavigable
{
    /// Moves the sideways choice by one in <paramref name="direction"/> (-1 or +1).
    void Step(int direction);
}
