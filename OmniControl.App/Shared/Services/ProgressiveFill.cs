namespace OmniControl.App.Shared.Services;

/// Shows a long list whose rows are heavy and cannot be virtualised (the main window's search results, each a live control): the first
/// screenful is shown at once, the rest is added in small batches after each frame (<see cref="DrawFirst"/>). A newer fill stops an
/// older one. The shown list is changed through <see cref="ListDiff"/>, never cleared.
public sealed class ProgressiveFill<T>
{
    /// Rows shown at once, and rows added per later batch.
    public const int FirstScreen = 20, Batch = 20;

    private readonly ObservableCollection<T> _target;
    private readonly Func<T, object> _keyOf;
    private readonly Action<Action> _later;
    private readonly int _firstCount, _batchCount;
    private int _generation;

    /// <param name="later">Runs a batch later; <see cref="DrawFirst.ThenWire"/> when null (tests give their own).</param>
    /// <param name="first">Rows shown at once; <see cref="FirstScreen"/> when 0 (a list of heavier rows asks for fewer).</param>
    /// <param name="batch">Rows per later batch; <see cref="Batch"/> when 0.</param>
    public ProgressiveFill(ObservableCollection<T> target, Func<T, object> keyOf, Action<Action>? later = null, int first = 0, int batch = 0)
    {
        (_target, _keyOf) = (target, keyOf);
        _later = later ?? DrawFirst.ThenWire;
        (_firstCount, _batchCount) = (first > 0 ? first : FirstScreen, batch > 0 ? batch : Batch);
    }

    public void Show(IReadOnlyList<T> wanted)
    {
        var generation = ++_generation;
        var first = Math.Min(_firstCount, wanted.Count);
        ListDiff.Apply(_target, wanted.Take(first).ToList(), _keyOf);
        if (first < wanted.Count) _later(() => Append(wanted, first, generation));
    }

    private void Append(IReadOnlyList<T> wanted, int from, int generation)
    {
        if (generation != _generation) return; // a newer list is being shown
        var to = Math.Min(from + _batchCount,wanted.Count);
        for (var i = from; i < to; i++) _target.Add(wanted[i]);
        if (to < wanted.Count) _later(() => Append(wanted, to, generation));
    }
}
