using CommunityToolkit.Mvvm.ComponentModel;

namespace OmniControl.App.OtherPcs;

/// <summary>Where a pairing stands on this PC.</summary>
public enum PairingPhase
{
    /// <summary>Not pairing.</summary>
    Closed,

    /// <summary>Open to pairing: other PCs on the network may pick this PC, and this PC may pick one of them.</summary>
    Open,

    /// <summary>This PC asked another to pair: the code on that PC is to be typed here.</summary>
    AskingCode,

    /// <summary>Another PC asked this one: the code is shown here and is to be typed on that PC.</summary>
    ShowingCode,

    /// <summary>Connecting or finishing.</summary>
    Working,

    /// <summary>The last pairing ended; <see cref="PairingState.Message"/> says how.</summary>
    Ended,
}

/// <summary>What the pairing panel shows (changed only on the UI thread, by <see cref="OtherPcsService"/>).</summary>
public sealed partial class PairingState : ObservableObject
{
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsClosed), nameof(IsOpen), nameof(IsAsking), nameof(IsShowing), nameof(IsWorking), nameof(IsEnded), nameof(IsActive))]
    private PairingPhase _phase;

    /// <summary>The New PC tab this pairing belongs to (its id); only that tab shows the code, the questions and the message.</summary>
    [ObservableProperty] private string _tabId = string.Empty;

    /// <summary>The other PC's name, once it is known.</summary>
    [ObservableProperty] private string _peerName = string.Empty;

    /// <summary>The 6 digits to type on the other PC (<see cref="PairingPhase.ShowingCode"/>).</summary>
    [ObservableProperty] private string _code = string.Empty;

    /// <summary>One plain sentence about how the last pairing ended or what is happening.</summary>
    [ObservableProperty] private string _message = string.Empty;

    public bool IsClosed => Phase == PairingPhase.Closed;
    public bool IsOpen => Phase == PairingPhase.Open;
    public bool IsAsking => Phase == PairingPhase.AskingCode;
    public bool IsShowing => Phase == PairingPhase.ShowingCode;
    public bool IsWorking => Phase == PairingPhase.Working;
    public bool IsEnded => Phase == PairingPhase.Ended;

    /// <summary>The panel is shown.</summary>
    public bool IsActive => Phase != PairingPhase.Closed;
}

/// <summary>A PC on the network that is open to pairing now.</summary>
public sealed record NearbyPc(string Id, string Name, string Address, int Port);
