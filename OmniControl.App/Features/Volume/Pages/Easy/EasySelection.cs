namespace OmniControl.App.Features.Volume.Pages.Easy;

/// Which goal cards are ticked on the welcome. Pure state (no screen): a goal is ticked or not, Next can start only when at least one is,
/// and the goals that run are always in the order of the cards (<see cref="EasyGoals.All"/>), whatever order they were ticked in.
public sealed class EasySelection
{
    private readonly HashSet<string> _ids = new();

    /// The ticked goals' ids (the "What you will need" box reads them).
    public IReadOnlyCollection<string> Ids => _ids;

    public bool CanStart => _ids.Count > 0;

    /// The ticked goals in the order of the cards.
    public IReadOnlyList<EasyGoal> Goals => EasyGoals.All.Where(g => _ids.Contains(g.Id)).ToList();

    /// Ticks a goal that is not ticked and unticks one that is; returns whether it is ticked now.
    public bool Toggle(string id)
    {
        if (_ids.Remove(id)) return false;
        _ids.Add(id);
        return true;
    }

    public void Clear() => _ids.Clear();
}
