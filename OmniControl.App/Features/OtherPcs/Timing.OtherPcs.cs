namespace OmniControl.App.Shared.Services;

// The intervals and limits of the link between paired PCs and waking them; a part of Timing in Shared/Services, so every call stays Timing.X.
public static partial class Timing
{
    /// How often this PC announces itself on the local network while Other PCs is in use.
    public static readonly TimeSpan PcBeaconEvery = TimeSpan.FromSeconds(4);

    /// How long "Pair a PC" keeps this PC open to a pairing (then it closes by itself).
    public static readonly TimeSpan PcPairingOpen = TimeSpan.FromMinutes(2);

    /// Each step of a connection's start (a pairing's messages, a session's proof) must arrive within this time, or the connection is dropped.
    public static readonly TimeSpan PcHandshakeStep = TimeSpan.FromSeconds(8);

    /// How often a paired PC that is seen but not connected is dialled, and how often a live link is checked and sends its keep-alive.
    public static readonly TimeSpan PcLinkCheck = TimeSpan.FromSeconds(5);

    /// The route probe (an authenticated ping answered with a pong): how often it runs for each paired PC, how long it waits, and how long a success counts as connected.
    public static readonly TimeSpan PcProbeEvery = TimeSpan.FromSeconds(15), PcProbeTimeout = TimeSpan.FromSeconds(4), PcProbeFresh = TimeSpan.FromSeconds(40);

    /// A link that has sent nothing (not even its keep-alive) for this long is closed.
    public static readonly TimeSpan PcLinkSilence = TimeSpan.FromSeconds(30);

    /// A change in what is shared waits this long for more changes before it is sent (a page's actions register in a burst).
    public static readonly TimeSpan PcShareSettle = TimeSpan.FromMilliseconds(400);

    /// A paired PC may start at most <see cref="PcRunBurstMax"/> actions here within this time; more is treated as a runaway and dropped.
    public static readonly TimeSpan PcRunBurstWindow = TimeSpan.FromSeconds(5);

    public const int PcRunBurstMax = 8;

    /// A level dragged here for another PC is sent at most this often (only the newest value); the other PC accepts at most <see cref="PcSetBurstMax"/> sets per <see cref="PcSetBurstWindow"/>.
    public static readonly TimeSpan PcSetEvery = TimeSpan.FromMilliseconds(120);

    public static readonly TimeSpan PcSetBurstWindow = TimeSpan.FromSeconds(2);

    public const int PcSetBurstMax = 40;

    /// How long a request for another PC's device set-ups waits for the answer, and how many such requests one paired PC may make of this one within the window.
    public static readonly TimeSpan PcDevicesWait = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan PcDevicesBurstWindow = TimeSpan.FromSeconds(10);

    public const int PcDevicesBurstMax = 4;

    /// How long a request of the settings import (the list of areas, or one area's files) waits for the answer, and how many such requests one paired PC may make of this one within the window (one per area, so a whole import fits).
    public static readonly TimeSpan PcImportWait = TimeSpan.FromSeconds(20);

    public static readonly TimeSpan PcImportBurstWindow = TimeSpan.FromSeconds(30);

    public const int PcImportBurstMax = 40;

    /// A card of another PC's state ignores that PC's reports for this long after the user moved it here (the echo of the user's own change).
    public static readonly TimeSpan PcEchoGrace = TimeSpan.FromSeconds(1);

    /// At most this many connections may be in their start (before they proved who they are) at once, and this many from one address.
    public const int PcHandshakesMax = 4, PcHandshakesPerAddress = 2;

    /// At most this many PCs heard on the network are remembered (beacons of more are dropped), and this many beacons a second are read.
    public const int PcSeenMax = 64, PcBeaconsPerSecond = 20;

    /// How long the network addresses the beacons go to are kept before they are listed again.
    public static readonly TimeSpan PcBroadcastRefresh = TimeSpan.FromMinutes(1);

    /// How often a Tailscale-paired PC that is offline gets the Tailscale checks again, how long to wait after a fix (start, sign in) before reading Tailscale again, and how long to wait before trying to open a Tailscale link again after one failed.
    public static readonly TimeSpan PcTailscaleRecheck = TimeSpan.FromSeconds(30), PcTailscaleAfterFix = TimeSpan.FromSeconds(3), PcTailscaleSwitchBackoff = TimeSpan.FromMinutes(2);

    /// Waking another PC: one wake signal per press, then up to <see cref="WakeWait"/> of waiting for the PC to be reachable (never sent again by itself); a relay answers within <see cref="WakeAskWait"/>.
    public static readonly TimeSpan WakeWait = TimeSpan.FromSeconds(120), WakeAskWait = TimeSpan.FromSeconds(6), WakePoll = TimeSpan.FromMilliseconds(500);

    /// How long a press of Wake (its button or key) may take to reach the signal before it no longer counts.
    public static readonly TimeSpan WakeGrantLife = TimeSpan.FromSeconds(5);

    /// How long a PC about to sleep or shut down waits for its "going away" notice to be sent, and how often it re-reads its own wake facts.
    public static readonly TimeSpan WakeNoticeWait = TimeSpan.FromMilliseconds(1500), WakeFactsEvery = TimeSpan.FromMinutes(5);

    /// A paired PC may ask this one for wake help (relay, check, fix, test) at most <see cref="WakeAskBurstMax"/> times per window.
    public static readonly TimeSpan WakeAskBurstWindow = TimeSpan.FromSeconds(30);

    public const int WakeAskBurstMax = 6;

    /// How long the PC that asked waits for another PC's answer to a wake fix (its own prompt and the Windows prompt wait for a person), and for a test: the sleep, then the wake.
    public static readonly TimeSpan WakeFixWait = TimeSpan.FromMinutes(3), WakeSleepWait = TimeSpan.FromSeconds(90), WakeTestDelay = TimeSpan.FromSeconds(2);

    /// A press of Test wake counts for its one wake until the other PC answered and went to sleep (the longest those take), and no longer.
    public static readonly TimeSpan WakeTestGrantLife = WakeFixWait + WakeSleepWait + WakeGrantLife;
}
