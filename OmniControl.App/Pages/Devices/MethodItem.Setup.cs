using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Devices;

namespace OmniControl.App.Pages.Devices;

// A method's set-up (docs/mockups/smart-devices-setup-vs-ready.html part 2): its numbered stages, one open at a time, and the Test stage. The
// device item opens and closes the set-up (DeviceItem.Setup.cs); the stages and the tests are this method's own.
public sealed partial class MethodItem
{
    private SetupStages _scriptStages;
    private readonly SetupStages _simulationStages;

    /// <summary>The stages of the method's kind.</summary>
    public SetupStages Stages => IsSimulation ? _simulationStages : _scriptStages;

    public IReadOnlyList<SetupStageItem> SimulationStageItems => _simulationStages.Items;
    public IReadOnlyList<SetupStageItem> ScriptStageItems => _scriptStages.Items;

    public bool CanFinish => Stages.AllDone;

    /// <summary>The set-up opens: the tests start again and the first stage not done opens.</summary>
    public void BeginSetup()
    {
        ResetTests();
        Stages.OpenNext();
    }

    /// <summary>The set-up opens at the teaching stage (the Ready view's Re-teach).</summary>
    public void OpenTeachStage() => _simulationStages.Open(SimulationTeachStage);

    /// <summary>A stage's own action finished it: the next stage that is not done opens.</summary>
    private void AdvanceSetup()
    {
        Changed();
        if (Stages.Items.FirstOrDefault(s => s.IsOpen) is { IsDone: true }) Stages.OpenNext();
    }

    // ---- The Test stage (both kinds) ----

    /// <summary>Which of the simulation's On, Off and Toggle passed (checked against the taught state), and whether the scripts' test passed.</summary>
    [ObservableProperty] private bool _onPassed;
    [ObservableProperty] private bool _offPassed;
    [ObservableProperty] private bool _togglePassed;
    [ObservableProperty] private bool _scriptPassed;

    /// <summary>A test ran but nothing read the change back: "Did it switch? Yes / No".</summary>
    [ObservableProperty] private bool _asksIfSwitched;

    /// <summary>The Test stage's one plain line (what failed, or what to try after "No"); empty when there is nothing to say.</summary>
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasTestNote))] private string _testNote = string.Empty;

    public bool HasTestNote => TestNote.Length > 0;

    private void ResetTests() => (OnPassed, OffPassed, TogglePassed, ScriptPassed, AsksIfSwitched, TestNote) = (false, false, false, false, false, string.Empty);

    [RelayCommand]
    private Task TestOn() => TestSimulation(DevicePower.On);

    [RelayCommand]
    private Task TestOff() => TestSimulation(DevicePower.Off);

    [RelayCommand]
    private Task TestToggle() => TestSimulation(DevicePower.Toggle);

    /// <summary>One of the simulation's tests, by this method only: it passes only when the reading confirmed the state afterwards.</summary>
    private async Task TestSimulation(DevicePower power)
    {
        var problem = await _service.RunAsync(_device, power, Record);
        var passed = problem is null && _runtime.Confirmed;
        switch (power)
        {
            case DevicePower.On: OnPassed = passed; break;
            case DevicePower.Off: OffPassed = passed; break;
            default: TogglePassed = passed; break;
        }
        TestNote = problem ?? string.Empty;
        AdvanceSetup();
    }

    /// <summary>The scripts' test: off, then on. Done at once when the template's State command read both back; otherwise the stage asks.</summary>
    [RelayCommand]
    private async Task TestScript()
    {
        if (!await SettlePasswordForTest()) return;
        var (problem, confirmed) = await _service.TestScriptAsync(_device, Record);
        ScriptPassed = confirmed;
        AsksIfSwitched = problem is null && !confirmed;
        TestNote = problem ?? string.Empty;
        AdvanceSetup();
    }

    [RelayCommand]
    private void SwitchedYes()
    {
        AsksIfSwitched = false;
        ScriptPassed = true;
        TestNote = string.Empty;
        AdvanceSetup();
    }

    [RelayCommand]
    private void SwitchedNo()
    {
        AsksIfSwitched = false;
        TestNote = "Check the address and sign-in, or pick another template (or try Advanced…), then test again.";
    }
}
