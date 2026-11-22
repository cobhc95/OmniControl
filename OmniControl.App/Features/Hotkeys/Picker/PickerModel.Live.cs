using OmniControl.App.Features.Hotkeys.Actions;

namespace OmniControl.App.Features.Hotkeys.Picker;

/// A list a picker follows while it is open: <see cref="Read"/> gives the entries now, <see cref="Watch"/> and <see cref="Unwatch"/> hang
/// a handler on the event that says the master lists behind it changed (raised on the UI thread).
public sealed record LiveList(Func<IReadOnlyList<PickChoice>> Read, Action<Action> Watch, Action<Action> Unwatch);

/// A live list with its window title and search words, for a picker every opener shows the same way (the "Choose a trigger or state" window).
public sealed record PickList(string Title, string SearchText, LiveList Live);

// Following a live list: while the window is open the entries are read again whenever their master lists change (a new device, a
// renamed script, a removed action), keeping the open section and the highlighted entry when they are still there.
public sealed partial class PickerModel
{
    private LiveList? _live;
    private bool _replacing;

    /// A picker of <paramref name="list"/> that follows it, on <paramref name="stored"/> (the entry the slot has now; empty for none).
    public static PickerModel Following(PickList list, string stored, string noun)
    {
        var model = On(list.Live.Read(), stored, noun, list.SearchText);
        model.Follow(list.Live);
        return model;
    }

    /// A picker of <paramref name="choices"/> on <paramref name="stored"/> (a slot's entry with its settings; empty for none).
    public static PickerModel On(IReadOnlyList<PickChoice> choices, string stored, string noun, string? searchText = null)
    {
        var (action, values) = MouseAction.Split(stored);
        var current = stored.Length == 0 ? null : choices.FirstOrDefault(c => c.Matches(action));
        return new PickerModel(PickChoice.Group(choices), current, values, currentParameter: current?.ParameterOf(action) ?? string.Empty)
        {
            Noun = noun,
            SearchText = searchText,
        };
    }

    /// Reads the list again whenever <paramref name="list"/> says it changed, until the window closes.
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
        var copy = ActiveRow?.IsCopy == true;
        var groups = PickChoice.Group(live.Read());
        _groups = groups.Select(g => g with { Choices = g.Choices.ToList() }).ToList();
        _index = null;
        if (_current is { } current) _current = _groups.SelectMany(g => g.Choices).FirstOrDefault(c => c.Spec == current.Spec) ?? current;
        _sections = null;
        _recentChoices = null;
        _presetChoices = null;
        _favouriteChoices = null;
        OnPropertyChanged(nameof(Sections));

        _replacing = true;
        var section = keepSection is null ? -1 : _groups.FindIndex(g => g.Name == keepSection);
        if (section >= 0) SectionIndex = section;
        if (FavouritesLead == 0) InFavourites = false;
        var rows = Build();
        var at = keepSpec is null ? -1 : IndexIn(rows, keepSpec, copy);
        ShowRows(rows, () => at >= 0 ? at : FirstEntry(), sync: false);
        _replacing = false;
        OnPropertyChanged(nameof(SectionHighlight)); // the sections list was read anew: its selection is shown again

        if (at >= 0 && ActiveChoice is { } same && _editing is not null)
        {
            _editing = same; // the same entry, read anew: its settings and values on the pane stay as they are
            SettingsTitle = TitleOf(same);
            ShowStatus(same);
        }
        else SyncSettings();
    }
}
