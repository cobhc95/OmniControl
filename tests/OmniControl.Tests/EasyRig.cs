using OmniControl.App.Features.Volume.Pages.Easy;
using OmniControl.App.Features.Volume.Pages.Easy.Flow;

namespace OmniControl.Tests;

/// The Easy tests' mixer: chains, effects power, routes and combined devices in memory (null key = the master).
internal sealed class EasyFakeMixer : IEasyMixer
{
    private readonly Dictionary<string, List<FxSlot>> _chains = new();
    private readonly Dictionary<string, bool> _on = new();
    private readonly Dictionary<string, EffectsRoute> _routes = new();
    private readonly List<CombinedDevice> _combined = new();
    public EffectsRoute MasterMethod { get; private set; } = EffectsRules.MasterAutomatic;
    public List<string> PlaysOn { get; private set; } = new();

    public List<FxSlot> FxChain(string? key) => _chains.TryGetValue(key ?? "master", out var c) ? c : _chains[key ?? "master"] = new List<FxSlot>();
    public bool IsFxOn(string? key) => _on.GetValueOrDefault(key ?? "master");
    public void SetFxOn(string? key, bool on) => _on[key ?? "master"] = on;
    public void SaveFx(string? key) { }
    public string InputKey(string deviceId, string name) => MixerService.InputPrefix + deviceId;
    public EffectsRoute FxRouteOf(string key) => _routes.GetValueOrDefault(key, EffectsRules.RouteAutomatic);
    public void SetFxRoute(string key, EffectsRoute route) => _routes[key] = route;
    public IReadOnlyList<CombinedDevice> Combined => _combined;
    public void AddCombined(CombinedDevice device) => _combined.Add(device);
    public void SetMasterMethod(EffectsRoute route) => MasterMethod = route;
    public void SetMasterPlaysOn(IEnumerable<string> targets) => PlaysOn = targets.ToList();
}

/// Records which fixes were carried out; <see cref="Effect"/> gives each one's answer (none by default).
internal sealed class EasyFakeActions : IEasyActions
{
    public List<string> Ran { get; } = new();
    public Func<EasyFix, string?>? Effect { get; set; }

    public Task<string?> RunAsync(EasyFix fix, EasyAnswers answers, EasyFacts facts)
    {
        Ran.Add(fix.Id);
        return Task.FromResult(Effect?.Invoke(fix));
    }
}

/// One Easy goal's flow on made-up facts: the answers, the fixes run, every question asked (answered with <see cref="Agree"/>), the
/// engine choices and the ways out to the cards and the page. Nothing touches Windows.
internal sealed class EasyRig
{
    public EasyFacts Facts { get; set; }
    public EasyFakeActions Actions { get; } = new();
    public bool Agree { get; set; } = true;
    public List<EasyConfirm> Asked { get; } = new();
    public int ToCards { get; private set; }
    public int ToPage { get; private set; }
    public EasyAnswers Answers { get; } = new();
    public List<EasyEngine> Chosen { get; } = new();
    public EasyFlowViewModel Flow { get; }

    public EasyRig(string goalId, EasyFacts? facts = null, EasyEngine? remembered = null)
    {
        Facts = facts ?? new EasyFacts();
        Answers.Engine = remembered;
        var services = new EasyFlowServices(() => Facts, Actions, question =>
        {
            Asked.Add(question);
            return Task.FromResult(Agree);
        });
        Flow = new EasyFlowViewModel(EasyGoals.All.Single(g => g.Id == goalId), EasyFlows.Of(goalId)!, Answers, services,
            (kind, changed) => new EasyQuestionViewModel(kind, Answers, changed, null, null), () => ToCards++, () => ToPage++, Chosen.Add);
    }

    /// Gets to the Engine step of a goal whose questions are already answered enough.
    public EasyRig AtEngine()
    {
        while (!Flow.IsEngine) Flow.NextCommand.Execute(null);
        return this;
    }

    public EasyEngineItem Card(EasyEngine engine) => Flow.EngineCards.Single(c => c.Engine == engine);
}
