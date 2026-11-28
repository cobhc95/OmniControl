using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;
using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.Displays.Pages;

/// Night light's own service, made by the displays' service: the scheduler that applies the warm tint immediately and runs its optional
/// schedule rule, the model its cards, actions and states read, its actions (Toggle, On, Off and the schedule rule's action) and its states. UI thread.
internal sealed class WarmLightService : IDisposable
{
    private const string ActionId = "displays.sw.warmlight", ScheduleActionId = ActionId + ".schedule", StateId = "displays.warmlight", StateWords = "Turns Night light on or off straight away.";
    private const string WarmingId = StateId + ".active", ScheduleStateId = StateId + ".schedule";

    private readonly IActionRegistry _hotkeys;
    private readonly IStateRegistry _states;
    private readonly WarmLightScheduler _scheduler;
    private readonly string _title;

    public WarmLightService(SoftwareDisplayService software, DisplaySettingsStore store, FeatureRulesSlot rules, IActionRegistry hotkeys, IStateRegistry states, string title)
    {
        (_hotkeys, _states, _title) = (hotkeys, states, title);
        _scheduler = new WarmLightScheduler(software, store, rules);
        Model = new WarmLightModel(_scheduler, store, rules);
        hotkeys.Register(Actions());
        states.Register(new StateEntry(StateId, WarmLightModel.Label, title, StateKind.OnOff, () => StateValue.Of(Model.IsOn), StateWords,
            Keywords: "warm light night schedule evening blue light filter", Set: value => Model.SetOn(value.Number is { } on && on != 0),
            Short: "Night light", Icon: Icons.IconLibrary.Moon, SuggestedIcons: new[] { Icons.IconLibrary.Moon, "sun-moon", "eye" }));
        states.Register(new StateEntry(ScheduleStateId, WarmLightModel.ScheduleLabel, title, StateKind.OnOff, () => StateValue.Of(Model.IsScheduled),
            "Whether Night light's automatic schedule is enabled.", Keywords: "warm light night schedule timer evening blue light filter",
            Set: value => Model.SetScheduled(value.Number is { } on && on != 0), Short: "Night schedule", Icon: Icons.IconLibrary.Moon));
        states.Register(new StateEntry(WarmingId, "Warm light active", title, StateKind.OnOff, () => StateValue.Of(_scheduler.IsWarming),
            "Night light is requesting a warm tint now; the display driver may refuse it.", Keywords: "warm light night schedule evening blue light filter", Icon: Icons.IconLibrary.Moon));
        _scheduler.Changed += OnChanged;
        _scheduler.Start();
    }

    public WarmLightModel Model { get; }

    /// Applies the warming again (the settings were read from disk).
    public void Update() => _scheduler.Update();

    private void OnChanged()
    {
        _states.Notify(new[] { StateId, ScheduleStateId, WarmingId });
        FollowFadeTitle();
    }

    /// Toggle, On and Off for Night light and Schedule night light (checked against their live states), then the schedule rule's own action.
    private IEnumerable<HotkeyAction> Actions()
    {
        var family = new OnOffFamily(ActionId, WarmLightModel.Label, StateId);
        var toggle = DisplayActions.Once(ActionId, "Toggle night light", "warm light night schedule evening blue light filter", _title, DisplayControls.SoftwareGroup,
            VcpSections.Colour, _ => Model.Toggle(), devices: null, icon: Icons.IconLibrary.Moon, family: family.Family, familyRole: OnOffFamily.ToggleRole);
        var scheduleFamily = new OnOffFamily(ScheduleActionId, WarmLightModel.ScheduleLabel, ScheduleStateId);
        var scheduleToggle = DisplayActions.Once(ScheduleActionId, "Toggle schedule night light", "warm light night schedule timer evening morning auto", _title, DisplayControls.SoftwareGroup,
            VcpSections.Colour, _ => Model.ToggleScheduled(), devices: null, icon: Icons.IconLibrary.Moon, family: scheduleFamily.Family, familyRole: OnOffFamily.ToggleRole);
        return family.Siblings(toggle, Model.SetOn).Prepend(toggle)
            .Concat(scheduleFamily.Siblings(scheduleToggle, Model.SetScheduled).Prepend(scheduleToggle))
            .Append(FadeAction());
    }

    /// The Night light schedule rule's own action, named with the temperature it warms to; it is made again when that changes (its keys stay, by id).
    private HotkeyAction FadeAction() =>
        new(WarmLightScheduler.FadeId, FadeTitle, _title, "warm light night fade schedule evening blue light filter kelvin temperature", _ => _scheduler.Fade(),
            group: DisplayControls.SoftwareGroup, subgroup: VcpSections.Colour) { FeatureRule = true, Icon = Icons.IconLibrary.Moon };

    private string FadeTitle => $"Turn night light on ({Model.Kelvin} K)";

    private void FollowFadeTitle()
    {
        if (_hotkeys.Find(WarmLightScheduler.FadeId) is not { } action || action.Title == FadeTitle) return;
        _hotkeys.Unregister(action.Id);
        _hotkeys.Register(FadeAction());
    }

    public void Dispose()
    {
        _scheduler.Changed -= OnChanged;
        _states.Unregister(new[] { StateId, ScheduleStateId, WarmingId });
        _scheduler.Dispose();
    }
}
