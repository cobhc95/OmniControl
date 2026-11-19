namespace OmniControl.App.Shared.Contracts;

/// The rules (by id) that led to an action running, in order: rule A ran an action, which started rule B, which ran this one. It travels
/// with every run that a rule causes (<see cref="Features.Hotkeys.HotkeyRegistry.Chain"/>), so a rule never starts itself again and a chain of rules that
/// start each other is cut after a limit (<see cref="Services.Timing.AutomationChainMax"/>). Null = a person or Windows started it.
/// <paramref name="ByPerson"/> = the chain's first rule was started by a person (its own hotkey, a mouse button, Run now, a person on
/// another PC), not by a trigger, so what it does is the person's request (the power actions' start-up grace does not hold it back).
public sealed record RunChain(IReadOnlyList<string> Rules, bool ByPerson = false)
{
    public static RunChain Start(string rule, bool byPerson = false) => new(new[] { rule }, byPerson);

    public RunChain Then(string rule) => new(Rules.Append(rule).ToArray(), ByPerson);

    /// True when <paramref name="rule"/> may start from a run with this chain: it is not in it, and the chain is shorter than <paramref name="max"/>.
    public static bool MayStart(RunChain? chain, string rule, int max) => chain is null || (!chain.Rules.Contains(rule) && chain.Rules.Count < max);

    /// The chain a rule's own run carries: <paramref name="rule"/> after the chain that started it (a new chain, started by a person or not, when none).
    public static RunChain After(RunChain? chain, string rule, bool byPerson = false) => chain is null ? Start(rule, byPerson) : chain.Then(rule);

    /// Who started a run, for a log line: the rules by id (a request from another PC is one of them), or, with no chain, a person.
    public static string Describe(RunChain? chain) =>
        chain is null
            ? "a key, mouse button, tray menu or Run now"
            : $"rule {string.Join(" > ", chain.Rules)} ({(chain.ByPerson ? "started by a person" : "started by a trigger")})";
}
