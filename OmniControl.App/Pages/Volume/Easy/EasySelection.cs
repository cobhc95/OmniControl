namespace OmniControl.App.Pages.Volume.Easy;

/// <summary>
/// Which goal cards are ticked on the welcome. Pure state (no screen): a goal is ticked or not, Next can start only when at least one is,
/// and the goals that run are always in the order of the cards (<see cref="EasyGoals.All"/>), whatever order they were ticked in.
/// </summary>
public sealed class EasySelection
{
    private readonly HashSet<string> _ids = new();

    /// <summary>The ticked goals' ids (the "What you will need" box reads them).</summary>
    public IReadOnlyCollection<string> Ids => _ids;

    public bool CanStart => _ids.Count > 0;

    /// <summary>The ticked goals in the order of the cards.</summary>
    public IReadOnlyList<EasyGoal> Goals => EasyGoals.All.Where(g => _ids.Contains(g.Id)).ToList();

    /// <summary>Ticks a goal that is not ticked and unticks one that is; returns whether it is ticked now.</summary>
    public bool Toggle(string id)
    {
        if (_ids.Remove(id)) return false;
        _ids.Add(id);
        return true;
    }

    public void Clear() => _ids.Clear();
}
