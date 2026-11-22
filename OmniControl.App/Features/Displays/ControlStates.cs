using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Displays;

/// A level control's state on one display (a model's, which its card shows): its value, where a double-click puts it, and why it cannot be used.
internal sealed class LevelState
{
    public LevelState(int value, int resetValue, string? reason) => (Value, ResetValue, Reason) = (value, resetValue, reason);

    public int Value { get; set; }
    public int ResetValue { get; set; }

    /// Why the control cannot be used now; null: it can.
    public string? Reason { get; set; }

    public bool IsAvailable => Reason is null;
}

/// A choice control's state on one display: the values it lists, the one it shows, and why it cannot be used.
internal sealed class ChoiceState
{
    public const string Waiting = "...";

    public IReadOnlyList<VcpChoice> Values { get; set; } = Array.Empty<VcpChoice>();
    public string Selected { get; set; } = Waiting;

    /// Why the control cannot be used now; null: it can.
    public string? Reason { get; set; }

    public bool IsAvailable => Reason is null;

    public IReadOnlyList<string> Names => Values.Select(v => v.Name).ToList();
}
