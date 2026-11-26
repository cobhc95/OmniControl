namespace OmniControl.App.Features.Hotkeys;

/// One step of a level action: <paramref name="Delta"/> (signed, in the level's own units) and, when <paramref name="Snap"/> is set, a request to
/// land on a round stop (a multiple of the step) instead of adding the step to wherever the level is.
public readonly record struct LevelMove(int Delta, bool Snap = false);
