namespace OmniControl.App.Hotkeys;

/// <summary>
/// The rules (by id) that led to an action running, in order: rule A ran an action, which started rule B, which ran this one. It travels
/// with every run that a rule causes (<see cref="HotkeyRegistry.Chain"/>), so a rule never starts itself again and a chain of rules that
/// start each other is cut after a limit (<see cref="Services.Timing.AutomationChainMax"/>). Null = a person or Windows started it.
/// </summary>
public sealed record RunChain(IReadOnlyList<string> Rules)
{
    public static RunChain Start(string rule) => new(new[] { rule });

    public RunChain Then(string rule) => new(Rules.Append(rule).ToArray());

    /// <summary>True when <paramref name="rule"/> may start from a run with this chain: it is not in it, and the chain is shorter than <paramref name="max"/>.</summary>
    public static bool MayStart(RunChain? chain, string rule, int max) => chain is null || (!chain.Rules.Contains(rule) && chain.Rules.Count < max);

    /// <summary>The chain a rule's own run carries: <paramref name="rule"/> after the chain that started it.</summary>
    public static RunChain After(RunChain? chain, string rule) => chain is null ? Start(rule) : chain.Then(rule);
}
