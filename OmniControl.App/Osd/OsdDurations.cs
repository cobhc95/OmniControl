namespace OmniControl.App.Osd;

/// <summary>
/// The OSD duration (<see cref="OsdSettings.Duration"/>) and its exceptions (<see cref="OsdSettings.Durations"/>): set the time, add, change,
/// reorder and remove exceptions. Every change is kept in the settings by <c>changed</c> (which saves and tells the listeners); the
/// pop-up asks <see cref="OsdDuration.For"/> through <see cref="For"/>.
/// </summary>
public sealed class OsdDurations
{
    private readonly OsdSettings _settings;
    private readonly Action _changed;

    public OsdDurations(OsdSettings settings, Action changed) => (_settings, _changed) = (settings, changed);

    /// <summary>"Everything else": the time of a pop-up no exception matches.</summary>
    public double EverythingElse => OsdDuration.Clamp(_settings.Duration);

    public IReadOnlyList<OsdDurationRule> Rules => _settings.Durations;

    public TimeSpan For(string element, string? program) => OsdDuration.For(_settings.Duration, _settings.Durations, element, program);

    public void SetEverythingElse(double seconds)
    {
        var clean = OsdDuration.Clamp(seconds);
        if (clean == _settings.Duration) return;
        _settings.Duration = clean;
        _changed();
    }

    /// <summary>Adds an exception at the end for <paramref name="kind"/> and <paramref name="key"/>, at the "Everything else" time.</summary>
    public OsdDurationRule Add(OsdDurationKind kind, string key, string name)
    {
        var rule = new OsdDurationRule { Kind = kind, Key = key, Name = name, Seconds = EverythingElse };
        _settings.Durations.Add(rule);
        _changed();
        return rule;
    }

    /// <summary>Makes the exception match another app or event.</summary>
    public void Retarget(OsdDurationRule rule, OsdDurationKind kind, string key, string name)
    {
        if (!_settings.Durations.Contains(rule)) return;
        (rule.Kind, rule.Key, rule.Name) = (kind, key, name);
        _changed();
    }

    public void SetSeconds(OsdDurationRule rule, double seconds)
    {
        var clean = OsdDuration.Clamp(seconds);
        if (!_settings.Durations.Contains(rule) || clean == rule.Seconds) return;
        rule.Seconds = clean;
        _changed();
    }

    public void Remove(OsdDurationRule rule)
    {
        if (_settings.Durations.Remove(rule)) _changed();
    }

    /// <summary>A drag of <paramref name="moved"/> rows (ascending) in front of the <paramref name="insertAt"/>-th row that stays (<see cref="Services.Reorder.Moved"/>).</summary>
    public void Move(IReadOnlyList<int> moved, int insertAt)
    {
        _settings.Durations = Services.Reorder.Moved(_settings.Durations, moved, insertAt);
        _changed();
    }
}
