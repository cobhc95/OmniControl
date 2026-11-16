using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Pages.Volume;

namespace OmniControl.Tests;

/// <summary>
/// Builds the Volume page's pieces in the order its constructor makes them (see the remarks on <see cref="VolumePageViewModel"/>), with
/// empty data (no devices, no programs, the engine's facts unknown), and syncs them as the first reads do: nothing may read a piece that
/// is not made yet (an earlier job crashed the app at start-up that way).
/// </summary>
public class VolumeConstructionTests
{
    [Fact]
    public void ThePiecesCanBeMadeAndSynced_InThePagesOrder_WithNothingPluggedIn()
    {
        // 1. The gates.
        var combineSpeakers = new ControlGate();
        var combineMicrophones = new ControlGate();

        // 2. The master row with its Plays on menu and Method menu (they reach the page only when they open).
        var opened = 0;
        var master = new MixerItem(MixerService.MasterKey, true)
        {
            Name = "Master",
            PlaysOn = new PlaysOnMenu(new ControlGate(), _ => opened++, marksCurrent: true),
            RouteChoice = RouteChoice.ForMaster(_ => { }, () => { }),
            FxWindowToggled = () => { },
        };

        // 3. The first reads: the master's columns and its Method menu's steps, from unknown facts.
        var status = EngineStatus.Off;
        var method = EffectsRules.ChipMethodOf(new RowMethodFacts(RowKind.Master, false, RunMethod.None, RunMethod.Apo));
        master.Method = MethodChip.Of(method);
        master.Fact = status.Fact(method);
        master.EngineLine = status.TipFor(EffectsRules.MethodFor(null));
        master.RouteChoice!.Sync(EffectsRules.MasterAutomatic, r => EffectsRules.MasterOfferOf(r, MasterFacts.Unknown, RunMethod.Apo));
        master.RouteChoice.SyncSteps(Array.Empty<CardAction>());
        master.PlaysOn!.Gate.Sync(OmniControl.App.Audio.Gate.Open);
        master.FxRuns = FxSlot.AnyRuns(master.FxOn, Array.Empty<FxSlot>());

        // 4. The Wiring row made over it, with its own Plays on menu.
        var wiring = new WiringRow(master, new PlaysOnMenu(new ControlGate(), _ => opened++, marksCurrent: false));
        wiring.Menu.Open();
        master.PlaysOn.Open();

        // 5. The other kinds of rows, empty.
        var microphone = new InputItem("none");
        microphone.Method = MethodChip.Of(EffectsRules.ChipMethodOf(new RowMethodFacts(RowKind.Microphone, false, RunMethod.None, RunMethod.Apo)));

        Assert.Equal("Equalizer APO", master.Method.Label);
        Assert.Equal(EffectsRules.NoEffects, master.FactLine);
        Assert.StartsWith("Effects off", master.EngineLine);
        Assert.False(master.RouteChoice.HasSteps);
        Assert.False(master.FxRuns);
        Assert.Equal(2, opened);
        Assert.True(combineSpeakers.IsAvailable && combineMicrophones.IsAvailable);
        Assert.Equal("Direct", microphone.Method.Label);
        Assert.True(microphone.PlaysOnIsFixed);
    }
}
