using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.States;
using OmniControl.App.Shared.Contracts;

namespace OmniControl.App.Features.Displays.Pages;

/// Warm light's own service, made by the displays' service: the scheduler that runs it through its automation rule, the model its cards, actions
/// and state read, its actions (Toggle, On, Off and the rule's own "Warm light fade to K") and its on/off state. UI thread.
internal sealed class WarmLightService : IDisposable
{
    private const string ActionId = "displays.sw.warmlight", StateId = "displays.warmlight", StateWords = "The page's Warm light at night switch.";

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
            Keywords: "warm light night schedule evening blue light filter", Set: value => Model.SetEnabled(value.Number is { } on && on != 0),
            Short: "Night light", Icon: Icons.IconLibrary.Moon, SuggestedIcons: new[] { Icons.IconLibrary.Moon, "sun-moon", "eye" }));
        _scheduler.Changed += OnChanged;
        _scheduler.Start();
    }

    public WarmLightModel Model { get; }

    /// Applies the warming again (the settings were read from disk).
    public void Update() => _scheduler.Update();

    private void OnChanged()
    {
        _states.Notify(StateId);
        FollowFadeTitle();
    }

    /// Toggle, On and Off (checked against its state), then the rule's own action.
    private IEnumerable<HotkeyAction> Actions()
    {
        var family = new OnOffFamily(ActionId, WarmLightModel.Label, StateId);
        var toggle = DisplayActions.Once(ActionId, "Toggle warm light at night", "warm light night schedule evening blue light filter", _title, DisplayControls.SoftwareGroup,
            VcpSections.Colour, _ => Model.Toggle(), devices: null, icon: Icons.IconLibrary.Moon, family: family.Family, familyRole: OnOffFamily.ToggleRole);
        return family.Siblings(toggle, Model.SetEnabled).Prepend(toggle).Append(FadeAction());
    }

    /// The warm-light rule's own action, named with the temperature it warms to; it is made again when that changes (its keys stay, by id).
    private HotkeyAction FadeAction() =>
        new(WarmLightScheduler.FadeId, FadeTitle, _title, "warm light night fade evening blue light filter kelvin temperature", _ => _scheduler.Fade(),
            group: DisplayControls.SoftwareGroup, subgroup: VcpSections.Colour) { FeatureRule = true, Icon = Icons.IconLibrary.Moon };

    private string FadeTitle => $"Warm light fade to {Model.Kelvin} K";

    private void FollowFadeTitle()
    {
        if (_hotkeys.Find(WarmLightScheduler.FadeId) is not { } action || action.Title == FadeTitle) return;
        _hotkeys.Unregister(action.Id);
        _hotkeys.Register(FadeAction());
    }

    public void Dispose()
    {
        _scheduler.Changed -= OnChanged;
        _states.Unregister(StateId);
        _scheduler.Dispose();
    }
}
