using OmniControl.Core.Engine;

namespace OmniControl.App.Automation;

/// <summary>What the conditions are asked about: the clock, the power source and the running programs. The programs are only looked up when a condition asks.</summary>
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

    /// <summary>True on battery, false when plugged in, null when Windows can't say.</summary>
    public bool? OnBattery { get; }

    /// <summary>Names of the running programs ("chrome"), see <see cref="Rules.ProgramNames.Normalize"/>.</summary>
    public IReadOnlySet<string> Programs => _programs.Value;
}
