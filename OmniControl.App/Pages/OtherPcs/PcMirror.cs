using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Controls;
using OmniControl.App.Dialogs;
using OmniControl.App.OtherPcs;
using OmniControl.App.Services;
using OmniControl.App.States;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.OtherPcs;

/// <summary>One group of another PC's basic set (Screens, Sound...): its group, drawn like any page's group, and the chips that run its actions. Placed by <see cref="FixedColumnsPanel"/> in the fixed column its <see cref="CommonGroup"/> names; a group the PC shares nothing in keeps its place and says "Not available".</summary>
public sealed record MirrorGroup(ItemGroup Group, IReadOnlyList<ChipItem> Actions, int Column, bool IsEmpty) : IColumnPlaced
{
    public bool HasActions => Actions.Count > 0;
}

/// <summary>
/// Another PC's basic set (docs/mockups/other-pcs.html): one group per entry of <see cref="CommonGroups.All"/>, always, in
/// that order and in that entry's fixed column (one that PC shares nothing in says "Not available"), each with real controls of the shared card kinds (a slider, a switch, a drop-down; a read-only fact when the PC does not let
/// a state be set) made from what the PC shares and read live from the master lists (<see cref="OtherPcsService.StatesOf"/>); a change
/// made here is sent to that PC, a change there arrives and moves the card. A screen's power state is its switch card and the group's
/// header has the all-screens master (<see cref="ScreenPowerSwitch"/>), as on the local Displays page. A read-only level (the battery) is
/// the header's muted text. Nothing here is listed by hand.
/// </summary>
public sealed partial class PcMirror : ObservableObject
{
    private const string AllScreensWords = "All displays";
    private readonly string _pcId;
    private readonly OtherPcsService _service;
    private readonly Func<DialogMessage, Task<bool>> _ask;
    private readonly Action<string> _report;
    private readonly Dictionary<string, Action<StateEntry>> _updaters = new();
    private readonly Dictionary<string, List<CardGroup>> _switches = new();
    private readonly Dictionary<string, DateTime> _edited = new();

    public PcMirror(string pcId, OtherPcsService service, Func<DialogMessage, Task<bool>> ask, Action<string> report) =>
        (_pcId, _service, _ask, _report) = (pcId, service, ask, report);

    public ObservableCollection<MirrorGroup> Groups { get; } = new();

    /// <summary>Makes every group again from what the PC shares now (its catalog changed). Every group of <see cref="CommonGroups.All"/> is always made, in that order, so none changes place.</summary>
    public void Rebuild()
    {
        _updaters.Clear();
        _switches.Clear();
        Groups.Clear();
        var states = _service.StatesOf(_pcId);
        var actions = _service.ActionsOf(_pcId);
        foreach (var common in CommonGroups.All)
        {
            var own = states.Where(s => CommonGroups.Resolve(s.Common) == common.Name).ToList();
            var chips = actions.Where(a => CommonGroups.Resolve(a.Common) == common.Name && !a.ScreensOff).Select(Chip).ToList();
            Groups.Add(Make(common, own, chips, actions.FirstOrDefault(a => CommonGroups.Resolve(a.Common) == common.Name && a.ScreensOff)));
        }
    }

    /// <summary>One state's value changed there: its card shows it at once (not while the user is moving it here), and a screen switch looks again.</summary>
    public void Update(StateEntry state)
    {
        if (_edited.TryGetValue(state.Id, out var when) && Grace.Within(when, Timing.PcEchoGrace)) return;
        if (_updaters.TryGetValue(state.Id, out var update)) update(state);
        if (_switches.TryGetValue(state.Id, out var groups)) foreach (var group in groups) group.RefreshMaster();
    }

    // ---- Groups ----

    private MirrorGroup Make(CommonGroup common, IReadOnlyList<RemoteState> states, List<ChipItem> chips, RemoteAction? screensOff)
    {
        var (name, icon) = (common.Name, common.Icon);
        var empty = states.Count == 0 && chips.Count == 0;
        var summary = states.Where(s => !s.Settable && s.Entry.Kind == StateKind.Level).ToList();
        var detail = states.Where(s => !s.Settable && s.Entry.Kind == StateKind.Choice && !IsNowPlaying(s.Entry)).ToList();
        var cards = states.Except(summary).Except(detail).OrderBy(Rank).ToList();
        var items = Cards(cards, chips).Cast<object>().ToList();
        var group = new ItemGroup($"OtherPcs/{_pcId}/{name}", name, items) { Icon = icon, ExpandedByDefault = true, NoMasterSwitch = empty || name == CommonGroups.Power };
        if (name == CommonGroups.Power) ShowBattery(group, summary.FirstOrDefault()?.Entry, detail.FirstOrDefault()?.Entry);
        else
            foreach (var state in summary)
            {
                group.Summary = state.Entry.Words(state.Entry.Read());
                _updaters[state.Entry.Id] = e => group.Summary = e.Words(e.Read());
            }
        AttachMaster(group, states, screensOff);
        return new MirrorGroup(group, chips, common.Column, empty);
    }

    /// <summary>The Power header's battery fact (icon, percent, tooltip) from the charge and status states; it follows either one.</summary>
    private void ShowBattery(CardGroup group, StateEntry? level, StateEntry? status)
    {
        void Show(StateEntry _) => group.Badge = BatteryBadge.Make(level, status);
        Show(level ?? status!);
        foreach (var entry in new[] { level, status }) if (entry is not null) _updaters[entry.Id] = Show;
    }

    /// <summary>Drop-downs first, then sliders, then switches, then the Now playing line: the order the mock-up shows a group's cards in.</summary>
    private static int Rank(RemoteState state) => IsNowPlaying(state.Entry) ? 3 : state.Entry.Kind switch { StateKind.Choice => 0, StateKind.Level => 1, _ => 2 };

    /// <summary>The master of a group's screens: each screen that has a power state of its own, and the PC's "Turn off displays" action for turning all off when it is shared.</summary>
    private void AttachMaster(CardGroup group, IReadOnlyList<RemoteState> states, RemoteAction? screensOff)
    {
        var screens = states.Where(s => s.ScreenPower && s.Settable).ToList();
        if (screens.Count == 0) return;
        group.UseSwitch(ScreenPowerSwitch.All(() => screens.Select(ScreenOf).ToList(), () => screensOff is null,
            screensOff is null ? null : () => _service.Run(screensOff.Action),
            knownOff: () => screens.All(s => ScreenOf(s).IsOn() == false)));
        foreach (var screen in screens)
        {
            if (!_switches.TryGetValue(screen.Entry.Id, out var list)) _switches[screen.Entry.Id] = list = new List<CardGroup>();
            list.Add(group);
        }
    }

    private static ScreenPowerSwitch.Screen ScreenOf(RemoteState power) =>
        new(() => power.Entry.Read() is { Number: { } n } ? n != 0 : null, on => power.Entry.Set?.Invoke(StateValue.Of(on)));

    // ---- Cards: one real control per state kind ----

    /// <summary>The group's cards; a drop-down beside a slider (Sound's output and volume) becomes the slider's leading chip, the on/off states
    /// that have state-button words and icons (Sound's and the mic's mute) become the slider's state buttons (docs/mockups/sound-mute-buttons.html),
    /// and the media actions join the Now playing card.</summary>
    private List<IEditorItem> Cards(List<RemoteState> states, List<ChipItem> chips)
    {
        var level = states.FirstOrDefault(s => s.Settable && s.Entry.Kind == StateKind.Level && !IsPerScreen(s));
        var lead = level is null ? null : states.FirstOrDefault(s => s.Settable && s.Entry.Kind == StateKind.Choice && !IsNowPlaying(s.Entry));
        var buttons = level is null ? new List<RemoteState>() : states.Where(IsStateButton).ToList();
        var made = new List<IEditorItem>();
        var shown = new HashSet<string>();
        foreach (var state in states.Where(s => s != lead && !buttons.Contains(s)))
        {
            if (IsPerScreen(state))
            {
                if (shown.Add(state.Name)) made.Add(ScreenSlider(state.Name, states.Where(s => IsPerScreen(s) && s.Name == state.Name).ToList()));
            }
            else if (IsNowPlaying(state.Entry)) made.Add(NowPlayingCard(state.Entry, chips));
            else if (state == level) made.Add(Slider(state.Entry, LabelOf(state), lead is null ? null : Choice(lead.Entry, LabelOf(lead)), buttons.Select(StateButton).ToList()));
            else made.Add(Card(state, states.Count == 1));
        }
        return made;
    }

    /// <summary>An on/off state its owner gives state-button words and icons (<see cref="StateEntry.Compact"/>, <see cref="StateEntry.StateIcons"/>).</summary>
    private static bool IsStateButton(RemoteState state) =>
        state.Settable && !state.ScreenPower && state.Entry.Kind == StateKind.OnOff && state.Compact is not null && state.StateIcons is not null;

    private static string LabelOf(RemoteState state) =>
        state.Section.Length == 0 ? state.Name : state.ScreenPower ? state.Section : $"{state.Section}: {state.Name}";

    /// <summary>A level of one screen (its section is the screen's name): the screens' same-named levels become ONE card with a display chip.</summary>
    private static bool IsPerScreen(RemoteState state) =>
        state.Settable && !state.ScreenPower && state.Entry.Kind == StateKind.Level && state.Section.Length > 0;

    private static bool IsNowPlaying(StateEntry entry) => entry.Id.EndsWith(":" + NowPlayingText.StateId, StringComparison.Ordinal);

    private IEditorItem Card(RemoteState state, bool alone)
    {
        var entry = state.Entry;
        var label = LabelOf(state);
        if (!state.Settable) return Fact(entry, label);
        return entry.Kind switch
        {
            StateKind.Level => Slider(entry, label),
            StateKind.OnOff => Toggle(entry, !alone && state.Compact is { } compact ? compact : label, half: !alone),
            _ => Choice(entry, label),
        };
    }

    private void Send(StateEntry entry, StateValue value)
    {
        _edited[entry.Id] = DateTime.UtcNow;
        entry.Set?.Invoke(value);
    }

    private SliderItem Slider(StateEntry entry, string label, ChoiceItem? lead = null, IReadOnlyList<StateButtonItem>? extras = null)
    {
        var slider = new SliderItem(label, (int)Math.Round(entry.Read()?.Number ?? entry.Min), v =>
        {
            Send(entry, StateValue.Of(v));
            return OperationResult.Ok();
        }, _report, entry.Min, entry.Max, entry.Unit, defaultValue: null) { Icon = entry.ShownIcon, Lead = lead, AsKnob = true, Extras = extras ?? Array.Empty<StateButtonItem>() };
        _updaters[entry.Id] = e => { if (e.Read()?.Number is { } n) slider.Load((int)Math.Round(n)); };
        return slider;
    }

    /// <summary>A mute state as a state button: its name while on ("Sound muted"), its compact name and "on" while off ("Sound on"); the tooltip names the device it acts on.</summary>
    private StateButtonItem StateButton(RemoteState state)
    {
        var entry = state.Entry;
        var button = new StateButtonItem(IsOn(entry.Read()), state.Name, $"{state.Compact} {StateValue.OnWord}", state.StateIcons!.On, state.StateIcons.Off,
            on => Send(entry, StateValue.Of(on))) { Tip = entry.Read()?.Subject ?? string.Empty };
        _updaters[entry.Id] = e =>
        {
            button.Tip = e.Read()?.Subject ?? string.Empty;
            button.Load(IsOn(e.Read()));
        };
        return button;
    }

    private static bool IsOn(StateValue? reading) => reading?.Number is not (null or 0);

    /// <summary>One knob for a level every screen of the PC has (Brightness): its display chip picks "All displays" (every screen moves together, the knob shows the first one's level) or one screen; the pick is kept for this PC. Each screen is still its own shared state.</summary>
    private SliderItem ScreenSlider(string name, List<RemoteState> screens)
    {
        var options = new List<string> { AllScreensWords };
        options.AddRange(screens.Select(s => s.Section));
        var saved = _service.ScreenPick(_pcId, name);
        var picked = options.Contains(saved) ? saved : AllScreensWords;
        SliderItem? slider = null;
        IEnumerable<RemoteState> Targets(string pick) => pick == AllScreensWords ? screens : screens.Where(s => s.Section == pick);
        int LevelOf(string pick) => (int)Math.Round(Targets(pick).First().Entry.Read()?.Number ?? screens[0].Entry.Min);
        var choice = new ChoiceItem(name, options, picked, pick =>
        {
            _service.SetScreenPick(_pcId, name, pick == AllScreensWords ? string.Empty : pick);
            slider?.Load(LevelOf(pick));
        }) { FirstApart = true, Icon = screens[0].Entry.ShownIcon };
        slider = new SliderItem(name, LevelOf(picked), v =>
        {
            foreach (var target in Targets(choice.Selected)) Send(target.Entry, StateValue.Of(v));
            return OperationResult.Ok();
        }, _report, screens[0].Entry.Min, screens[0].Entry.Max, screens[0].Entry.Unit, defaultValue: null) { Icon = screens[0].Entry.ShownIcon, Lead = choice, AsKnob = true };
        foreach (var screen in screens) _updaters[screen.Entry.Id] = _ => slider.Load(LevelOf(choice.Selected));
        return slider;
    }

    private ToggleItem Toggle(StateEntry entry, string label, bool half)
    {
        var toggle = new ToggleItem(label, IsOn(entry.Read()), on => Send(entry, StateValue.Of(on))) { Half = half, Icon = entry.ShownIcon, Subject = entry.Read()?.Subject ?? string.Empty };
        _updaters[entry.Id] = e =>
        {
            toggle.Subject = e.Read()?.Subject ?? string.Empty;
            toggle.Load(IsOn(e.Read()));
        };
        return toggle;
    }

    private ChoiceItem Choice(StateEntry entry, string label)
    {
        var options = entry.Choices?.Invoke() ?? new List<string>();
        var choice = new ChoiceItem(label, options, entry.Read()?.Text ?? string.Empty, picked => Send(entry, StateValue.Of(picked))) { Icon = entry.ShownIcon };
        _updaters[entry.Id] = e => choice.Load(e.Choices?.Invoke() ?? new List<string>(), e.Read()?.Text ?? string.Empty);
        return choice;
    }

    /// <summary>Now playing as one row inside Sound (docs/mockups/sound-group-compact.html): the title and its state tag over the muted "artist · app" line and the PC's three media actions as icon pills at the right, Play/Pause in the accent while it plays; "Nothing playing" in the same row, with the pills, when no app plays.</summary>
    private FactItem NowPlayingCard(StateEntry entry, List<ChipItem> chips)
    {
        var own = NowPlayingText.ActionIds.Select(id => chips.FirstOrDefault(c => c.Key.EndsWith(":" + id, StringComparison.Ordinal))).OfType<ChipItem>().ToList();
        foreach (var chip in own) chips.Remove(chip); // the card holds them: the group shows no second row for them
        var playPause = own.FirstOrDefault(c => c.Key.EndsWith(":" + NowPlayingText.PlayPauseId, StringComparison.Ordinal));
        var fact = new FactItem(entry.Name) { Icon = entry.ShownIcon, Stacked = true };
        void Show(StateEntry e)
        {
            var (title, detail, playing, nothing) = NowPlayingText.Read(e.Read());
            fact.Value = title;
            fact.Note = nothing ? NowPlayingText.NothingWords : detail;
            fact.Pills = own;
            fact.Tags = NowPlayingText.StateTags(playing, nothing);
            if (playPause is null) return;
            (playPause.Name, playPause.Icon) = NowPlayingText.PlayPausePill(playing);
            playPause.IsSelected = playing;
        }
        Show(entry);
        _updaters[entry.Id] = Show;
        return fact;
    }

    private FactItem Fact(StateEntry entry, string label)
    {
        var fact = new FactItem(label) { Icon = entry.ShownIcon, Value = entry.Words(entry.Read()) };
        _updaters[entry.Id] = e => fact.Value = e.Words(e.Read());
        return fact;
    }

    // ---- Actions ----

    private ChipItem Chip(RemoteAction action) =>
        new(action.Action.Title, new RelayCommand(() => _ = Guard.RunAsync($"Other PCs: {action.Action.Title}", () => RunAsync(action))), action.Action.ShownIcon) { Key = action.Action.Id };

    private async Task RunAsync(RemoteAction action)
    {
        var pc = _service.Find(_pcId)?.Name ?? "That PC";
        var title = action.Action.Title;
        if (action.ConfirmRun && !await _ask(new DialogMessage($"{title} {pc}?", $"{title} will be done on \"{pc}\" now.", title))) return;
        _service.Run(action.Action);
    }
}
