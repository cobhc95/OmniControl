namespace OmniControl.App.Features.OtherPcs;

/// Where a pairing stands on this PC.
public enum PairingPhase
{
    /// Not pairing.
    Closed,

    /// Open to pairing: other PCs on the network may pick this PC, and this PC may pick one of them.
    Open,

    /// This PC asked another to pair: the code on that PC is to be typed here.
    AskingCode,

    /// Another PC asked this one: the code is shown here and is to be typed on that PC.
    ShowingCode,

    /// Connecting or finishing.
    Working,

    /// The last pairing ended; <see cref="PairingState.Message"/> says how.
    Ended,
}

/// What the pairing panel shows (changed only on the UI thread, by <see cref="OtherPcsService"/>).
public sealed partial class PairingState : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsClosed), nameof(IsOpen), nameof(IsAsking), nameof(IsShowing), nameof(IsWorking), nameof(IsEnded), nameof(IsActive))]
    private PairingPhase _phase;

    /// The New PC tab this pairing belongs to (its id); only that tab shows the code, the questions and the message.
    [ObservableProperty] private string _tabId = string.Empty;

    /// The other PC's name, once it is known.
    [ObservableProperty] private string _peerName = string.Empty;

    /// The 6 digits to type on the other PC (<see cref="PairingPhase.ShowingCode"/>).
    [ObservableProperty] private string _code = string.Empty;

    /// One plain sentence about how the last pairing ended or what is happening.
    [ObservableProperty] private string _message = string.Empty;

    public bool IsClosed => Phase == PairingPhase.Closed;
    public bool IsOpen => Phase == PairingPhase.Open;
    public bool IsAsking => Phase == PairingPhase.AskingCode;
    public bool IsShowing => Phase == PairingPhase.ShowingCode;
    public bool IsWorking => Phase == PairingPhase.Working;
    public bool IsEnded => Phase == PairingPhase.Ended;

    /// The panel is shown.
    public bool IsActive => Phase != PairingPhase.Closed;
}

/// A PC on the network that is open to pairing now.
public sealed record NearbyPc(string Id, string Name, string Address, int Port);
