using OmniControl.App.Features.Volume.Pages;

namespace OmniControl.Tests;

/// The Mixer row's columns: the Method chip for every kind of row and state
/// (<see cref="EffectsRules.ChipMethodOf"/>), the short fact under it, the Plays on column per row kind, the FX button's glow, the "Next sound setup" hotkey (its order and its old id), and a pick on a Sound setup card.
public class MixerRowTests
{
    private static RowMethodFacts Program(bool runs, RunMethod running = RunMethod.None, RunMethod master = RunMethod.Apo,
        bool follows = true, EffectsRoute? route = null, bool cableFree = false) =>
        new(RowKind.Program, runs, running, master, follows, route, cableFree);

    // ---- the Method chip ----

    [Fact]
    public void WhatRunsNow_AlwaysWins()
    {
        Assert.Equal(RunMethod.Recording, EffectsRules.ChipMethodOf(Program(true, RunMethod.Recording, route: EffectsRules.RouteCable)));
        Assert.Equal(RunMethod.ApoCable, EffectsRules.ChipMethodOf(new RowMethodFacts(RowKind.Program, true, RunMethod.ApoCable, RunMethod.Bus)));
    }

    [Theory]
    [InlineData(RunMethod.Bus, true, RunMethod.Bus)]   // master on the cable bus
    [InlineData(RunMethod.Apo, true, RunMethod.Apo)]   // master in Equalizer APO with effects
    [InlineData(RunMethod.Apo, false, RunMethod.Apo)]  // ... with no effects: still the method it is set to, never "Direct"
    public void TheMaster(RunMethod master, bool runs, RunMethod chip) =>
        Assert.Equal(chip, EffectsRules.ChipMethodOf(new RowMethodFacts(RowKind.Master, runs, RunMethod.None, master)));

    [Fact]
    public void TheMasterOnAutomatic_WithNothingToRun_SaysAutomatic()
    {
        Assert.Equal(RunMethod.Automatic, EffectsRules.ChipMethodOf(new RowMethodFacts(RowKind.Master, false, RunMethod.None, RunMethod.Apo, Route: EffectsRules.MasterAutomatic)));
        Assert.Equal(RunMethod.Apo, EffectsRules.ChipMethodOf(new RowMethodFacts(RowKind.Master, true, RunMethod.None, RunMethod.Apo, Route: EffectsRules.MasterAutomatic)));
        Assert.Equal(RunMethod.Bus, EffectsRules.ChipMethodOf(new RowMethodFacts(RowKind.Master, false, RunMethod.None, RunMethod.Bus, Route: EffectsRules.MasterAutomatic)));
    }

    [Fact]
    public void AProgramWithoutEffects_PassesThroughTheBus_OrShowsItsChosenMethod()
    {
        Assert.Equal(RunMethod.Bus, EffectsRules.ChipMethodOf(Program(false, master: RunMethod.Bus, follows: true, route: EffectsRules.RouteCable)));
        Assert.Equal(RunMethod.Automatic, EffectsRules.ChipMethodOf(Program(false, master: RunMethod.Bus, follows: false, route: EffectsRules.RouteAutomatic)));
        Assert.Equal(RunMethod.Cable, EffectsRules.ChipMethodOf(Program(false, route: EffectsRules.RouteCable)));
    }

    [Fact]
    public void AProgramWithEffects_TakesItsChosenMethod()
    {
        Assert.Equal(RunMethod.Cable, EffectsRules.ChipMethodOf(Program(true, route: EffectsRules.RouteCable)));
        Assert.Equal(RunMethod.Recording, EffectsRules.ChipMethodOf(Program(true, route: EffectsRules.RouteCapture)));
        Assert.Equal(RunMethod.ApoCable, EffectsRules.ChipMethodOf(Program(true, route: EffectsRules.RouteApoCable)));
        Assert.Equal(RunMethod.Cable, EffectsRules.ChipMethodOf(Program(true, route: EffectsRules.RouteAutomatic, cableFree: true)));
        Assert.Equal(RunMethod.Recording, EffectsRules.ChipMethodOf(Program(true, route: EffectsRules.RouteAutomatic, cableFree: false)));
    }

    [Fact]
    public void Microphones_UseTheirCable_CombinedDevicesTheirCable()
    {
        Assert.Equal(RunMethod.Cable, EffectsRules.ChipMethodOf(new RowMethodFacts(RowKind.Microphone, true, RunMethod.None, RunMethod.Bus)));
        Assert.Equal(RunMethod.Cable, EffectsRules.ChipMethodOf(new RowMethodFacts(RowKind.Microphone, true, RunMethod.None, RunMethod.Apo)));
        Assert.Equal(RunMethod.Apo, EffectsRules.ChipMethodOf(new RowMethodFacts(RowKind.Microphone, false, RunMethod.None, RunMethod.Bus, Own: RunMethod.Apo)));
        Assert.Equal(RunMethod.Cable, EffectsRules.ChipMethodOf(new RowMethodFacts(RowKind.Combined, false, RunMethod.None, RunMethod.Bus)));
    }

    [Fact]
    public void TheChip_HasOneWordAndOneColourPerMethod()
    {
        Assert.Equal(("Equalizer APO", true, false, false), Look(RunMethod.Apo));
        Assert.Equal(("Cable", false, true, false), Look(RunMethod.Cable));
        Assert.Equal(("Cable bus", false, true, false), Look(RunMethod.Bus));
        Assert.Equal(("Recording", false, false, true), Look(RunMethod.Recording));
        Assert.Equal(("Direct", false, false, false), Look(RunMethod.None));
        Assert.Equal(("Automatic", false, false, false), Look(RunMethod.Automatic));
        Assert.True(MethodChip.Of(RunMethod.ApoCable).IsApo);

        static (string, bool, bool, bool) Look(RunMethod m)
        {
            var chip = MethodChip.Of(m);
            return (chip.Label, chip.IsApo, chip.IsCable, chip.IsRecording);
        }
    }

    // ---- the fact under the chip ----

    [Fact]
    public void TheFact_IsShort_AndNeverRepeatsTheMethod()
    {
        var running = new EngineStatus(EngineState.Running, "x", null, StatusKind.Running, RunMethod.Recording, 70);
        Assert.Equal("70 ms", running.Fact(RunMethod.Recording));
        Assert.Equal("no delay", new EngineStatus(EngineState.Running, "x", null, StatusKind.Running, RunMethod.Apo).Fact(RunMethod.Apo));
        Assert.Equal("70 ms", new EngineStatus(EngineState.Running, "x", DelayMs: 70).Fact(RunMethod.Cable)); // a running state with no fact of its own
        Assert.Equal(EffectsRules.NoEffects, new EngineStatus(EngineState.Off, "x", null, StatusKind.Direct).Fact(RunMethod.Apo));
        Assert.Equal("Starts when it plays", new EngineStatus(EngineState.Waiting, "x", null, StatusKind.StartsWhenPlays).Fact(RunMethod.Recording));
        Assert.Equal("No free cable", new EngineStatus(EngineState.Unavailable, "x", null, StatusKind.NoFreeCable).Fact(RunMethod.Cable));
        Assert.Equal(EffectsRules.NoDelay, EngineStatus.Off.Fact(RunMethod.None));
        Assert.Equal(EffectsRules.NoEffects, EngineStatus.Off.Fact(RunMethod.Bus));
    }

    // ---- the row's columns per kind of row ----

    [Fact]
    public void PlaysOn_IsAMenuForTheMasterAndPrograms_AJumpForCombinedDevices_WordsForAMicrophone()
    {
        var master = new MixerItem(MixerService.MasterKey, true) { PlaysOn = Menu() };
        var program = new MixerItem("app", false) { PlaysOn = Menu() };
        var microphone = new InputItem("mic-1");
        var combined = new ComboItem(new CombinedDevice { Name = "Both", Kind = CombinedKind.Speakers }) { WiringRequested = () => { } };

        Assert.True(master.HasPlaysOnMenu && program.HasPlaysOnMenu);
        Assert.False(master.PlaysOnOpensWiring || master.PlaysOnIsFixed);
        Assert.True(combined.PlaysOnOpensWiring && !combined.HasPlaysOnMenu && !combined.PlaysOnIsFixed);
        Assert.True(microphone.PlaysOnIsFixed && !microphone.HasPlaysOnMenu && !microphone.PlaysOnOpensWiring);

        Assert.True(master.HasPan && program.HasPan && !microphone.HasPan);
        Assert.True(microphone.HasVolume && !combined.HasVolume);
        Assert.False(combined.HasEffects);
        Assert.False(microphone.HasMethodMenu); // no Method menu: the chip is words only
    }

    [Fact]
    public void PanWords_AndTheKnobsTooltipsCarryTheExactValue()
    {
        var row = new MixerItem("app", false);
        Assert.Equal("C", row.PanText);
        row.Pan = -20;
        Assert.Equal("L 20", row.PanText);
        Assert.Contains("L 20", row.PanTip);
        row.Pan = 35;
        Assert.Equal("R 35", row.PanText);
        row.Volume = 72;
        Assert.Equal("Volume: 72", row.VolumeValueTip);
    }

    [Fact]
    public void TheFactLine_ShowsTheTestFirst_ThenTheFact_AndOneLink()
    {
        var row = new MixerItem("app", false) { PlaysOn = Menu() };
        row.Fact = "70 ms";
        Assert.Equal("70 ms", row.FactLine);
        Assert.False(row.HasFactLink);
        var fix = new CardAction(EffectsRules.UseRecordingLabel, EffectsRules.UseRecordingTip, new CommunityToolkit.Mvvm.Input.RelayCommand(() => { }));
        row.Fix = fix;
        Assert.Same(fix, row.FactLink);
        row.TestNote = "Test passed · 70 ms";
        Assert.Equal("Test passed · 70 ms", row.FactLine);
    }

    [Fact]
    public void TheFxButtonOpensAndClosesTheWindow()
    {
        var toggles = 0;
        var row = new MixerItem("app", false) { FxWindowToggled = () => toggles++ };
        row.ToggleWindowCommand.Execute(null);
        row.ToggleWindowCommand.Execute(null);
        Assert.Equal(2, toggles);
        Assert.Equal("Opens or closes this row's FX window", row.FxTip);
    }

    [Fact]
    public void TheFxButtonGlows_OnlyWhenAnEffectRuns()
    {
        var on = new FxSlot { Enabled = true };
        var off = new FxSlot { Enabled = false };
        Assert.True(FxSlot.AnyRuns(true, new[] { off, on }));
        Assert.False(FxSlot.AnyRuns(true, new[] { off }));
        Assert.False(FxSlot.AnyRuns(false, new[] { on }));
    }

    [Fact]
    public void TheEngineLine_SaysHowTheEffectsRun_WithNoSetupOrModeName()
    {
        var line = new EngineStatus(EngineState.Running, "Effects running", null, StatusKind.Running).LineFor(EffectsRules.MethodFor("app"));
        Assert.StartsWith("Effects ", line);
        Assert.DoesNotContain("Setup", line);
    }

    private static PlaysOnMenu Menu() => new(new ControlGate(), _ => { }, marksCurrent: true);
}
