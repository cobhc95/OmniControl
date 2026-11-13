using OmniControl.App.Services;

namespace OmniControl.App.Mouse;

/// <summary>What a button swap on trial goes back to: the swap state before it.</summary>
public sealed record SwapTrialJournal(bool SwappedBefore);

/// <summary>
/// Owns <c>pointer-swap-trial.json</c>, the crash journal of a button swap waiting for "Keep": written when the trial starts, removed
/// when it is kept or put back. Found at start-up, it means OmniControl ended during a trial, and the swap is put back then.
/// </summary>
internal static class SwapTrial
{
    private const string FileName = "pointer-swap-trial.json";

    public static void Begin(bool swappedBefore) => AppDataStore.Save(FileName, new SwapTrialJournal(swappedBefore));

    public static void End() => AppDataStore.Delete(FileName);

    /// <summary>The swap state to go back to after a trial a previous run left open, or null when there was none. Ends the journal.</summary>
    public static bool? TakeLeftOver()
    {
        var left = AppDataStore.Load<SwapTrialJournal>(FileName);
        if (left is not null) End();
        return left?.SwappedBefore;
    }
}
