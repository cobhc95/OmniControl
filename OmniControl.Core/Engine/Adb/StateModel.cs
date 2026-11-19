using System.Text.Json.Serialization;

namespace OmniControl.Core.Engine;

/// What a reading says about a device.
public enum DeviceState { Unknown, On, Off }

/// A reading: the state and how sure it is (0-1; below <see cref="CoreTiming.StateReadThreshold"/> the state is Unknown).
public sealed record StateReading(DeviceState State, double Confidence);

/// One thing that told on from off when the device was taught: a fact's two values, or a measure's two centres; its weight is how cleanly it did.
public sealed class StateSignal
{
    public string Key { get; set; } = string.Empty;
    public string? OnFact { get; set; }
    public string? OffFact { get; set; }
    public double[]? On { get; set; }
    public double[]? Off { get; set; }
    public double Weight { get; set; }
}

/// How a device shows on and off in its app, learned from several looks at each state (<see cref="StateSample"/>). A signal is kept when it is the
/// same in every look of one state and differs between the states; measures (colours, shape) are kept when the two states are well apart and
/// weighted by how far. A reading lets every signal vote, averages the votes per family (attributes, words, views, colour, shape) and the
/// families by their weights in <see cref="CoreTiming"/>, and is Unknown when the result is not clear enough. The card is found again by the
/// target (<see cref="StateTarget"/>). Saved with the device.
public sealed class StateModel
{
    public List<StateSample> OnSamples { get; set; } = new();
    public List<StateSample> OffSamples { get; set; } = new();
    public List<StateSignal> Signals { get; set; } = new();
    public StateTarget? Target { get; set; }

    /// How sure the recent readings were (smoothed); low means the app may have changed and the device wants teaching again.
    public double RecentConfidence { get; set; } = 1;

    [JsonIgnore] public bool IsTaught => Signals.Count > 0 && Target is not null;

    /// The recent readings were unsure, so the app's look has probably changed.
    [JsonIgnore] public bool IsDrifting => IsTaught && RecentConfidence < CoreTiming.StateDriftWarn;

    /// Learns the signals from the looks of both states. Null when it worked, otherwise a short phrase saying why it could not.
    public string? Derive()
    {
        Signals.Clear();
        Target = null;
        if (OnSamples.Count == 0 || OffSamples.Count == 0) return null;
        var all = OnSamples.Concat(OffSamples).ToList();
        foreach (var key in all.SelectMany(s => s.Facts.Keys).Distinct())
        {
            var on = OnSamples.Select(s => s.Facts.GetValueOrDefault(key, string.Empty)).Distinct().ToList();
            var off = OffSamples.Select(s => s.Facts.GetValueOrDefault(key, string.Empty)).Distinct().ToList();
            if (on.Count == 1 && off.Count == 1 && on[0] != off[0]) Signals.Add(new StateSignal { Key = key, OnFact = on[0], OffFact = off[0], Weight = 1 });
        }
        foreach (var key in all.SelectMany(s => s.Measures.Keys).Distinct().Where(k => all.All(s => s.Measures.ContainsKey(k))))
            if (MeasureSignal(key) is { } signal) Signals.Add(signal);
        Target = StateTarget.From(all);
        RecentConfidence = 1;
        var misread = OnSamples.Any(s => Read(s).State != DeviceState.On) || OffSamples.Any(s => Read(s).State != DeviceState.Off);
        if (Signals.Count > 0 && !misread) return null;
        Signals.Clear();
        return "on and off look the same to OmniControl";
    }

    private StateSignal? MeasureSignal(string key)
    {
        var on = OnSamples.Select(s => s.Measures[key]).ToList();
        var off = OffSamples.Select(s => s.Measures[key]).ToList();
        if (on.Concat(off).Select(v => v.Length).Distinct().Count() != 1) return null;
        double[] onCentre = Centre(on), offCentre = Centre(off);
        var between = Distance(key, onCentre, offCentre);
        var spread = Math.Max(on.Max(v => Distance(key, v, onCentre)), off.Max(v => Distance(key, v, offCentre)));
        if (between < MinSeparation(key) || spread * 2 >= between) return null;
        return new StateSignal { Key = key, On = onCentre, Off = offCentre, Weight = 1 - 2 * spread / between };
    }

    /// What a look says: On, Off or Unknown, and how sure (the weighted vote of the families, each the weighted vote of its signals).
    public StateReading Read(StateSample? sample)
    {
        if (sample is null || Signals.Count == 0) return new StateReading(DeviceState.Unknown, 0);
        double total = 0, weights = 0;
        foreach (var family in Signals.GroupBy(s => FamilyOf(s.Key)))
        {
            if (sample.PixelsMissing && family.Key is SignalFamily.Colour or SignalFamily.Shape) continue; // nothing to measure without a picture
            var score = family.Sum(s => s.Weight * Vote(s, sample)) / family.Sum(s => s.Weight);
            var weight = WeightOf(family.Key);
            total += weight * score;
            weights += weight;
        }
        var result = weights == 0 ? 0 : total / weights;
        var state = result >= CoreTiming.StateReadThreshold ? DeviceState.On : result <= -CoreTiming.StateReadThreshold ? DeviceState.Off : DeviceState.Unknown;
        return new StateReading(state, Math.Abs(result));
    }

    /// Adds one reading's confidence to <see cref="RecentConfidence"/>.
    public void Note(double confidence) =>
        RecentConfidence = RecentConfidence * (1 - CoreTiming.StateDriftSmoothing) + Math.Clamp(confidence, 0, 1) * CoreTiming.StateDriftSmoothing;

    /// The families that tell on from off, as words for the set-up ("switch state, words, colour").
    [JsonIgnore]
    public string Summary => string.Join(", ", Signals.Select(s => FamilyOf(s.Key)).Distinct().OrderBy(f => f).Select(f => f switch
    {
        SignalFamily.Attributes => "switch state",
        SignalFamily.Words => "words",
        SignalFamily.Views => "icons",
        SignalFamily.Colour => "colour",
        _ => "shape",
    }));

    private static double Vote(StateSignal signal, StateSample sample)
    {
        if (signal.OnFact is not null)
        {
            var value = sample.Facts.GetValueOrDefault(signal.Key, string.Empty);
            return value == signal.OnFact ? 1 : value == signal.OffFact ? -1 : 0;
        }
        if (signal.On is null || signal.Off is null || !sample.Measures.TryGetValue(signal.Key, out var x) || x.Length != signal.On.Length) return 0;
        double toOn = Distance(signal.Key, x, signal.On), toOff = Distance(signal.Key, x, signal.Off), between = Distance(signal.Key, signal.On, signal.Off);
        if (Math.Min(toOn, toOff) > between) return 0; // like neither state
        return Math.Clamp((toOff - toOn) / between, -1, 1);
    }

    private static double[] Centre(IReadOnlyList<double[]> values) =>
        Enumerable.Range(0, values[0].Length).Select(i => values.Average(v => v[i])).ToArray();

    /// The shape is compared by the share of its bits that differ; colours by straight distance on the hue-saturation disc.
    private static double Distance(string key, double[] a, double[] b) =>
        FamilyOf(key) == SignalFamily.Shape
            ? a.Zip(b, (x, y) => Math.Abs(x - y)).Sum() / a.Length
            : Math.Sqrt(a.Zip(b, (x, y) => (x - y) * (x - y)).Sum());

    private static double MinSeparation(string key) => key.StartsWith("shape:", StringComparison.Ordinal) ? CoreTiming.StateMinShapeSeparation
        : key.StartsWith("colourful:", StringComparison.Ordinal) ? CoreTiming.StateMinShareSeparation : CoreTiming.StateMinColourSeparation;

    private enum SignalFamily { Attributes, Words, Views, Colour, Shape }

    private static SignalFamily FamilyOf(string key) => key[..Math.Max(0, key.IndexOf(':'))] switch
    {
        "attr" => SignalFamily.Attributes,
        "view" => SignalFamily.Views,
        "colour" or "colourful" => SignalFamily.Colour,
        "shape" => SignalFamily.Shape,
        _ => SignalFamily.Words,
    };

    private static double WeightOf(SignalFamily family) => family switch
    {
        SignalFamily.Attributes => CoreTiming.StateWeightAttributes,
        SignalFamily.Words => CoreTiming.StateWeightWords,
        SignalFamily.Views => CoreTiming.StateWeightViews,
        SignalFamily.Colour => CoreTiming.StateWeightColour,
        _ => CoreTiming.StateWeightShape,
    };
}
