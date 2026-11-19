namespace OmniControl.App.Features.Input.Mouse;

/// What a button swap on trial goes back to: the swap state before it.
public sealed record SwapTrialJournal(bool SwappedBefore);

/// Owns <c>pointer-swap-trial.json</c>, the crash journal of a button swap waiting for "Keep": written when the trial starts, removed
/// when it is kept or put back. Found at start-up, it means OmniControl ended during a trial, and the swap is put back then.
internal static class SwapTrial
{
    private static readonly DataStore Store = DataStores.PointerSwapTrial;

    public static void Begin(bool swappedBefore) => AppDataStore.Save(Store, new SwapTrialJournal(swappedBefore));

    public static void End() => AppDataStore.Delete(Store);

    /// The swap state to go back to after a trial a previous run left open, or null when there was none. Ends the journal.
    public static bool? TakeLeftOver()
    {
        var left = AppDataStore.Load<SwapTrialJournal>(Store);
        if (left is not null) End();
        return left?.SwappedBefore;
    }
}
