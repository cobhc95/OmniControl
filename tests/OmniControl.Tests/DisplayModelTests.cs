using OmniControl.App.Shared.Contracts;
using System.Collections.Concurrent;
using OmniControl.App.Features.Displays;
using OmniControl.App.Features.Displays.Pages;
using OmniControl.Core.Interfaces;
using OmniControl.App.Shared.Ui.Pages;
using OmniControl.Core.Models;

namespace OmniControl.Tests;

/// The Displays models work with no page (the actions, states and presets act on them), and the page's cards are views that follow them
/// until they are dropped. Nothing here talks to a real display: the monitor and the ramps are fakes.
public class DisplayModelTests
{
    private sealed class FakeAccess : IHardwareAccess
    {
        public ConcurrentQueue<(byte Code, uint Value)> Writes { get; } = new();
        public bool IsLaptopPanel => false;
        public string UnsupportedReason => "Not reported";
        public MonitorCapabilities Probe() => MonitorCapabilities.None;
        public int? ReadPercent(byte code) => null;
        public OperationResult WritePercent(byte code, int percent) { Writes.Enqueue((code, (uint)percent)); return OperationResult.Ok(); }
        public uint? ReadRaw(byte code) => null;
        public OperationResult WriteRaw(byte code, uint value) { Writes.Enqueue((code, value)); return OperationResult.Ok(); }
    }

    private static readonly HardwareLevel Brightness = DisplayControls.Hardware.First(l => l.Code == VcpCode.Brightness);
    private static readonly HardwareLevel Unreported = DisplayControls.Hardware.First(l => l.Code != VcpCode.Brightness);
    private static readonly HardwareChoice Choice = DisplayControls.HardwareChoices[0];

    private static (HardwareDisplayModel Model, FakeAccess Access) ReadMonitor()
    {
        var access = new FakeAccess();
        var orientation = new DisplayOrientationModel(null, _ => null, "Monitor", null!, _ => { });
        var model = new HardwareDisplayModel("m1", "Monitor", access, orientation, new LevelStepper(), null!, null!, _ => { }, () => { });
        var code = Choice.Feature.Code;
        model.Answered(new MonitorCapabilities(
            new Dictionary<byte, VcpValue> { [VcpCode.Brightness] = new(40, 100, 2, false), [code] = new(1, 2, 0, false) },
            new Dictionary<byte, IReadOnlyList<VcpChoice>> { [code] = new[] { new VcpChoice(1, "A"), new VcpChoice(2, "B") } }));
        return (model, access);
    }

    private static void WaitFor(Func<bool> done)
    {
        var end = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < end) Thread.Sleep(10);
        Assert.True(done());
    }

    [Fact]
    public void AMonitorIsSetAndSteppedWithoutAPage()
    {
        var (model, access) = ReadMonitor();
        var changes = 0;
        model.Changed += () => changes++;

        Assert.Equal(40, model.LevelOf(Brightness));
        Assert.Null(model.LevelOf(Unreported));
        Assert.Contains(Brightness.Spec.Label, model.AvailableLabels);
        Assert.DoesNotContain(Unreported.Spec.Label, model.AvailableLabels);

        model.Set(Brightness, 170);
        Assert.Equal(Brightness.Spec.Max, model.LevelOf(Brightness));
        Assert.Equal(1, changes);
        WaitFor(() => access.Writes.Contains((VcpCode.Brightness, (uint)Brightness.Spec.Max)));

        model.Set(Unreported, 10);
        Assert.Null(model.LevelOf(Unreported));
        Assert.Equal(1, changes);

        Assert.Equal("A", model.Choice(Choice).Selected);
        model.NextChoice(Choice);
        Assert.Equal("B", model.Choice(Choice).Selected);
        WaitFor(() => access.Writes.Contains((Choice.Feature.Code, 2u)));
    }

    [Fact]
    public void APresetReadsAndWritesTheModel()
    {
        var (model, _) = ReadMonitor();
        var live = model.Snapshot();
        Assert.NotNull(live);
        Assert.Contains(live.Features, f => f.Code == VcpCode.Brightness && f.Value == 40);

        model.Write(new[] { new PlannedWrite(VcpCode.Brightness, VcpKind.Level, 65) });
        Assert.Equal(65, model.LevelOf(Brightness));
    }

    [Fact]
    public void TheWarmLightCardsFollowTheModelUntilDropped()
    {
        var store = new DisplaySettingsStore(null, _ => { });
        var rules = new FeatureRulesSlot();
        rules.Attach(new OneRule());
        var scheduler = new WarmLightScheduler(new SoftwareDisplayService(_ => throw new InvalidOperationException("no display is tracked"), keepRecovery: false), store, rules);
        var model = new WarmLightModel(scheduler, store, rules);
        var view = new WarmLightItem(model, _ => { });
        var button = (ButtonItem)view.Cards[0];
        var start = (ChoiceItem)view.Cards[1];

        model.SetEnabled(true);
        Assert.Equal("Turn off", button.ButtonText);
        var other = WarmLightModel.TimeNames.First(n => n != model.StartName);
        model.SetStart(other);
        Assert.Equal(other, start.Selected);

        var picked = WarmLightModel.TimeNames.First(n => n != other);
        start.Selected = picked; // the card writes the model
        Assert.Equal(picked, model.StartName);

        view.Dispose();
        model.SetEnabled(false);
        Assert.Equal("Turn off", button.ButtonText); // a dropped card no longer listens
        Assert.False(model.IsOn);
    }

    /// The rules as warm light sees them: one rule, kept in memory.
    private sealed class OneRule : IFeatureRules
    {
        private FeatureRuleView? _rule;

        public event Action? Changed;
        public event Action<string>? Ended { add { } remove { } }

        public FeatureRuleView? Find(string actionId) => _rule;

        public void Ensure(string actionId, string name, FeatureTrigger trigger, bool on) => Set(_rule ?? new FeatureRuleView(on, trigger as FeatureTrigger.Between, string.Empty));

        public void SetOn(string actionId, bool on) => Set(_rule! with { On = on });

        public void SetTrigger(string actionId, FeatureTrigger trigger) => Set(_rule! with { Window = trigger as FeatureTrigger.Between });

        public void Edit(string actionId) { }

        private void Set(FeatureRuleView rule)
        {
            _rule = rule;
            Changed?.Invoke();
        }
    }
}