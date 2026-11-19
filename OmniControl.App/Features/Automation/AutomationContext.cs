namespace OmniControl.App.Features.Automation;

/// What the conditions are asked about: the clock, the power source and the running programs. The programs are only looked up when a condition asks.
public sealed class AutomationContext
{
    private readonly Lazy<IReadOnlySet<string>> _programs;

    /// <param name="now">The moment to ask about; null = now (the tests give a fixed one).</param>
    public AutomationContext(Func<IReadOnlySet<string>> programs, DateTime? now = null)
    {
        _programs = new Lazy<IReadOnlySet<string>>(programs);
        Now = now ?? DateTime.Now;
        OnBattery = PowerState.IsOnBattery();
    }

    public DateTime Now { get; }

    /// True on battery, false when plugged in, null when Windows can't say.
    public bool? OnBattery { get; }

    /// Names of the running programs ("chrome"), see <see cref="Shared.Scripts.ProgramNames.Normalize"/>.
    public IReadOnlySet<string> Programs => _programs.Value;
}
