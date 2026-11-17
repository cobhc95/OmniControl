using System.Net.Sockets;
using OmniControl.App.Services;

namespace OmniControl.App.OtherPcs;

// Pairing: "Pair a PC" opens this PC for a while; the person then picks a PC (or types an address) on one PC, the other shows a code,
// and the person types it on the first (PcPairing has the exchange). Only one pairing runs at a time and a wrong code closes pairing.
public sealed partial class OtherPcsService
{
    private long _pairingOpenUntil; // UTC ticks; read by the network tasks
    private int _pairingBusy; // 1 while a pairing runs on this PC (either way round)
    private TaskCompletionSource<string?>? _codeWait;
    private CancellationTokenSource? _openTimer;

    /// <summary>What the pairing panel shows.</summary>
    public PairingState Pairing { get; } = new();

    private bool PairingIsOpen => Interlocked.Read(ref _pairingOpenUntil) > DateTime.UtcNow.Ticks;

    /// <summary>"Pair a PC": this PC becomes visible to pairing for <see cref="Timing.PcPairingOpen"/> and lists the PCs that are open too.</summary>
    public void OpenPairing()
    {
        if (StartupMode.Inert) return;
        StartNetwork();
        if (Pairing.Phase is PairingPhase.AskingCode or PairingPhase.ShowingCode or PairingPhase.Working) return;
        Interlocked.Exchange(ref _pairingOpenUntil, (DateTime.UtcNow + Timing.PcPairingOpen).Ticks);
        Pairing.Message = string.Empty;
        Pairing.Phase = PairingPhase.Open;
        _discovery?.AnnounceNow();
        CheckTailscale(string.Empty); // this PC's Tailscale address, so a PC pairing over the internet can be answered with it
        _openTimer?.Cancel();
        _openTimer = new CancellationTokenSource();
        var timer = _openTimer;
        _ = Guard.RunAsync("Other PCs: pairing window", async () =>
        {
            try
            {
                using var either = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, _stop.Token);
                await Task.Delay(Timing.PcPairingOpen, either.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            Post(() =>
            {
                if (Pairing.Phase == PairingPhase.Open) ClosePairing();
            });
        });
        RaiseChanged();
    }

    /// <summary>Closes the pairing panel and stops being visible to pairing; one running pairing is given up.</summary>
    public void ClosePairing()
    {
        Interlocked.Exchange(ref _pairingOpenUntil, 0);
        _openTimer?.Cancel();
        _codeWait?.TrySetResult(null);
        _codeWait = null;
        Pairing.Code = string.Empty;
        Pairing.Phase = PairingPhase.Closed;
        _discovery?.AnnounceNow();
        RaiseChanged();
    }

    /// <summary>Asks a PC found on the network to pair; the code it shows is then typed here (<see cref="SubmitCode"/>).</summary>
    public void PairWith(NearbyPc pc) => PairWith(pc.Address, pc.Port, PcRoute.Lan);

    /// <summary>Asks the PC at an address (Tailscale, ZeroTier or any other network) to pair; it must have "Pair a PC" open.</summary>
    public void PairWith(string address)
    {
        if (!TryParseAddress(address, out var host, out var port))
        {
            Pairing.Message = "That is not an address. Type a name or an address, such as 100.64.0.7.";
            return;
        }
        PairWith(host, port, PcRoute.Address);
    }

    /// <summary>Over the internet: asks the PC at a Tailscale name or address to pair; it must have "Pair a PC" open. Both PCs keep each other's Tailscale address, so either can dial afterwards.</summary>
    public void PairOverTailscale(string address)
    {
        if (!TryParseAddress(address, out var host, out var port))
        {
            Pairing.Message = "That is not an address. Pick a PC from the list or type its Tailscale name or address, such as 100.64.0.7.";
            return;
        }
        PairWith(host, port, PcRoute.Tailscale);
    }

    private void PairWith(string host, int port, PcRoute route)
    {
        if (Pairing.Phase is PairingPhase.AskingCode or PairingPhase.ShowingCode or PairingPhase.Working || Interlocked.CompareExchange(ref _pairingBusy, 1, 0) != 0) return;
        Pairing.Message = $"Connecting to {host}...";
        Pairing.Phase = PairingPhase.Working;
        RaiseChanged();
        var ownAddress = OwnTailscaleAddress;
        _ = Guard.RunAsync("Other PCs: pairing", async () =>
        {
            PairResult result;
            try
            {
                using var client = new TcpClient();
                using (var connect = PcWire.Step(_stop.Token)) await client.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
                client.NoDelay = true;
                result = await PcPairing.InitiateAsync(client.GetStream(), OwnId, _ownName, route == PcRoute.Tailscale ? ownAddress : string.Empty, AskCodeAsync, _stop.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (PcWire.IsNetworkError(ex))
            {
                result = PairResult.Failed($"Couldn't reach a PC that is open to pairing at {host}. Open \"Pair a PC\" on it too.");
            }
            Post(() => FinishPairing(result, route, host));
        });
    }

    /// <summary>The 6 digits shown on the other PC, typed here (a space in them is fine); a wrong code gives the pairing up.</summary>
    public void SubmitCode(string code) => _codeWait?.TrySetResult(code.Trim());

    /// <summary>Gives the pairing up (the code was not available, or the person changed their mind).</summary>
    public void CancelPairing() => ClosePairing();

    private async Task<string?> AskCodeAsync(string peerName)
    {
        var wait = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            _codeWait = wait;
            Pairing.PeerName = peerName;
            Pairing.Message = string.Empty;
            Pairing.Phase = PairingPhase.AskingCode;
            RaiseChanged();
        });
        try
        {
            return await wait.Task.WaitAsync(Timing.PcPairingOpen, _stop.Token).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    // The part of a pairing the listening PC runs (the connection's first byte said "pairing").
    private async Task RespondToPairingAsync(Stream stream)
    {
        if (!PairingIsOpen || Interlocked.CompareExchange(ref _pairingBusy, 1, 0) != 0) return; // not open, or busy: no answer at all
        PairResult result;
        try
        {
            result = await PcPairing.RespondAsync(stream, OwnId, _ownName, OwnTailscaleAddress, (name, code) => Post(() => ShowCode(name, code)), _stop.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (PcWire.IsNetworkError(ex))
        {
            result = PairResult.Failed("The other PC went away while pairing. Nothing was paired.");
        }
        Post(() => FinishPairing(result, PcRoute.Lan, string.Empty));
    }

    private void ShowCode(string peerName, string code)
    {
        Pairing.PeerName = peerName;
        Pairing.Code = $"{code[..3]} {code[3..]}";
        Pairing.Phase = PairingPhase.ShowingCode;
        RaiseChanged();
    }

    // The address the other PC gave (Tailscale) wins and makes the route Tailscale on either side; otherwise a LAN pairing keeps no address.
    private void FinishPairing(PairResult result, PcRoute route, string dialled)
    {
        var tailscale = result.PeerAddress.Length > 0 ? result.PeerAddress : route == PcRoute.Tailscale ? dialled : string.Empty;
        var address = route == PcRoute.Address ? dialled : string.Empty;
        if (result.PeerAddress.Length > 0) route = PcRoute.Tailscale;
        Interlocked.Exchange(ref _pairingBusy, 0);
        Interlocked.Exchange(ref _pairingOpenUntil, 0);
        _codeWait = null;
        Pairing.Code = string.Empty;
        if (Pairing.Phase == PairingPhase.Closed) return; // the person closed the panel meanwhile: whatever came of it is dropped
        if (result is { Paired: true, PeerId: { } id, PeerName: { } name, Secret: { } secret } && AddPair(id, name, secret, route, address, tailscale))
            Pairing.Message = $"Paired with {name}. Choose what each PC shares below.";
        else
            Pairing.Message = result.Problem ?? "Pairing did not finish. Nothing was paired.";
        Pairing.Phase = PairingPhase.Ended;
        _discovery?.AnnounceNow();
        RaiseChanged();
    }

    private bool AddPair(string id, string name, byte[] secret, PcRoute route, string address, string tailscale)
    {
        if (id == OwnId) return false;
        if (!_credentials.Write(CredentialTarget(id), Convert.ToBase64String(secret)))
        {
            _report("Windows would not keep the pairing key, so the PC was not paired.");
            return false;
        }
        _secrets[id] = secret;
        if (Find(id) is { } known)
        {
            known.Name = name; // paired again: the new key replaces the old, what is shared stays
            known.Route = route;
            known.Address = address;
            known.TailscaleAddress = tailscale;
        }
        else _settings.Pairs.Add(new PairedPc { Id = id, Name = name, Route = route, Address = address, TailscaleAddress = tailscale });
        CloseLink(id);
        Save();
        StartNetwork();
        CheckLinksSoon();
        return true;
    }
}
