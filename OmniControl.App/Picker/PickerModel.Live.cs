using OmniControl.App.Mouse;

namespace OmniControl.App.Picker;

/// <summary>
/// A list a picker follows while it is open: <see cref="Read"/> gives the entries now, <see cref="Watch"/> and <see cref="Unwatch"/> hang
/// a handler on the event that says the master lists behind it changed (raised on the UI thread).
/// </summary>
public sealed record LiveList(Func<IReadOnlyList<PickChoice>> Read, Action<Action> Watch, Action<Action> Unwatch);

// Following a live list: while the window is open the entries are read again whenever their master lists change (a new device, a
// renamed script, a removed action), keeping the open section and the highlighted entry when they are still there.
public sealed partial class PickerModel
{
    private LiveList? _live;
    private bool _replacing;

    /// <summary>Reads the list again whenever <paramref name="list"/> says it changed, until the window closes.</summary>
    public void Follow(LiveList list)
    {
        StopFollowing();
        _live = list;
        list.Watch(ReadAgain);
    }

    private void StopFollowing()
    {
        _live?.Unwatch(ReadAgain);
        _live = null;
    }

    private void ReadAgain()
    {
        if (_live is not { } live) return;
        var keepSection = SectionIndex < _groups.Count ? _groups[SectionIndex].Name : null;
        var keepSpec = ActiveChoice?.Spec;
        var groups = PickChoice.Group(live.Read());
        _groups = groups.Select(g => new PickGroup(g.Name, g.Choices.ToList(), g.Place)).ToList();
        if (_current is { } current) _current = _groups.SelectMany(g => g.Choices).FirstOrDefault(c => c.Spec == current.Spec) ?? current;
        _sections = null;
        _recentChoices = null;
        OnPropertyChanged(nameof(Sections));

        _replacing = true;
        var section = keepSection is null ? -1 : _groups.FindIndex(g => g.Name == keepSection);
        if (section >= 0) SectionIndex = section;
        Actions = Build();
        var at = keepSpec is null ? -1 : Actions.ToList().FindIndex(r => r.Choice?.Spec == keepSpec);
        ActionIndex = at >= 0 ? at : FirstEntry();
        _replacing = false;
        OnPropertyChanged(nameof(SectionHighlight)); // the sections list was read anew: its selection is shown again

        if (at >= 0 && ActiveChoice is { } same && _editing is not null)
        {
            _editing = same; // the same entry, read anew: its settings and values on the pane stay as they are
            SettingsTitle = same.Name;
        }
        else SyncSettings();
    }
}
