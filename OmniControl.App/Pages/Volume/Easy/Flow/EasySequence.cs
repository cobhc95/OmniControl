namespace OmniControl.App.Pages.Volume.Easy.Flow;

/// <summary>A goal that was set up, with what the user chose in it (the Easy page needs the ticked apps of "A different sound per app").</summary>
public sealed record EasyGoalResult(EasyGoal Goal, EasyAnswers Answers);

/// <summary>What a run of setups achieved, and the one line that says so.</summary>
public sealed record EasySummary(IReadOnlyList<EasyGoalResult> Completed, IReadOnlyList<EasyGoal> Skipped)
{
    public string Text
    {
        get
        {
            if (Completed.Count == 0) return "Nothing was set up.";
            var done = $"Set up: {string.Join(", ", Completed.Select(r => r.Goal.Short))}.";
            return Skipped.Count == 0 ? done : $"{done} Not set up: {string.Join(", ", Skipped.Select(g => g.Short))}.";
        }
    }
}

/// <summary>Where one goal's setup stands in a run of several, and what its buttons do (the sequence supplies them).</summary>
/// <param name="SkipEngine">The Engine screen is left out: an earlier goal already chose a way this goal can use, or this goal has nothing to choose.</param>
public sealed record EasyFlowPlace(int Number, int Count, bool SkipEngine, Action NextGoal, Action SkipGoal, Action Finish)
{
    public bool IsSequence => Count > 1;
    public bool HasNext => Number < Count;
}

/// <summary>
/// Runs the guided setups of the ticked goals one after another, in the order of the cards. It owns only the order: the current goal's
/// flow (made by the caller's factory), the way chosen so far (a later goal that can use it does not ask again), and which goals were
/// finished or skipped. A goal that cannot be completed is left with its skip button, so nothing half-done is ever applied for it; whatever an earlier goal
/// applied stays. Pure (no Windows), so the order and the reuse are tested with fakes.
/// </summary>
public sealed class EasySequence : IDisposable
{
    private readonly IReadOnlyList<EasyGoal> _goals;
    private readonly Func<EasyGoal, EasyAnswers, EasyFlowPlace, EasyFlowViewModel> _make;
    private readonly Action<EasyFlowViewModel?> _show;
    private readonly Action<EasySummary> _finished;
    private readonly List<EasyGoalResult> _completed = new();
    private readonly List<EasyGoal> _skipped = new();
    private EasyEngine? _carried;
    private EasyAnswers _answers = new();
    private int _index = -1;

    /// <param name="make">Makes the flow of one goal from its answers and its place.</param>
    /// <param name="show">Receives each goal's flow as it opens (null when the sequence is disposed).</param>
    /// <param name="finished">Called once, when the last goal's "Go to Volume" is pressed (or the last goal is skipped).</param>
    public EasySequence(IReadOnlyList<EasyGoal> goals, Func<EasyGoal, EasyAnswers, EasyFlowPlace, EasyFlowViewModel> make,
        Action<EasyFlowViewModel?> show, Action<EasySummary> finished)
    {
        _goals = goals;
        _make = make;
        _show = show;
        _finished = finished;
    }

    public EasyFlowViewModel? Current { get; private set; }

    /// <summary>The goal being set up now (null before <see cref="Start"/>).</summary>
    public EasyGoal? CurrentGoal => _index >= 0 && _index < _goals.Count ? _goals[_index] : null;

    public void Start() => Open(0);

    /// <summary>Whether a goal can use the way already chosen (so its Engine screen is left out).</summary>
    public static bool Reuses(EasyGoal goal, EasyEngine? carried) => carried is { } engine && goal.Engine is { } rule && rule.Allows(engine);

    private void Open(int index)
    {
        _index = index;
        var goal = _goals[index];
        _answers = new EasyAnswers();
        var reuse = Reuses(goal, _carried);
        if (reuse) _answers.Engine = _carried;
        var skipEngine = reuse || (_goals.Count > 1 && goal.Engine is { HasChoice: false });
        var place = new EasyFlowPlace(index + 1, _goals.Count, skipEngine, NextGoal, SkipGoal, Finish);
        var flow = _make(goal, _answers, place);
        var old = Current;
        Current = flow;
        _show(flow);
        old?.Dispose();
    }

    /// <summary>The way the goal just left settled on (a real choice only) is carried to the goals after it.</summary>
    private void Carry() => _carried = Current?.SettledEngine ?? _carried;

    private void NextGoal()
    {
        Complete();
        Carry();
        if (_index + 1 < _goals.Count) Open(_index + 1);
        else _finished(Summary());
    }

    private void SkipGoal()
    {
        _skipped.Add(_goals[_index]);
        Carry();
        if (_index + 1 < _goals.Count) Open(_index + 1);
        else _finished(Summary());
    }

    private void Finish()
    {
        Complete();
        _finished(Summary());
    }

    private void Complete() => _completed.Add(new EasyGoalResult(_goals[_index], _answers));

    private EasySummary Summary() => new(_completed.ToList(), _skipped.ToList());

    public void Dispose()
    {
        var current = Current;
        Current = null;
        current?.Dispose();
        _show(null);
    }
}
