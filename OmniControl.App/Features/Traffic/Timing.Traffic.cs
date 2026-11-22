namespace OmniControl.App.Shared.Services;

// The intervals of Network Traffic Control; a part of Timing in Shared/Services, so every call stays Timing.X.
public static partial class Timing
{
    /// The least time between two changes of Windows' network rules: a burst of chip clicks or actions is applied once.
    public static readonly TimeSpan ResourcesRulesGap = TimeSpan.FromSeconds(1);


    /// The network service stops itself after this long with no client, and ends its traffic session after this long with no read.
    public static readonly TimeSpan NetworkServiceIdle = TimeSpan.FromSeconds(60), NetworkServiceSpeedsIdle = TimeSpan.FromSeconds(30);

    /// How long OmniControl waits for the network service to take the connection after asking Windows to start it, for one answer
    /// (a change of Windows' rules can take a few seconds), and for it to stop when it is removed.
    public static readonly TimeSpan NetworkServiceConnectWait = TimeSpan.FromSeconds(5), NetworkServiceReplyWait = TimeSpan.FromSeconds(20),
        NetworkServiceStopWait = TimeSpan.FromSeconds(15);
}