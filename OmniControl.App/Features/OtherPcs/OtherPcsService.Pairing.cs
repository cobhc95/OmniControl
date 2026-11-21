using System.Net.Sockets;

namespace OmniControl.App.Features.OtherPcs;

// Pairing: a New PC tab opens this PC for a while; the person then picks a PC (or types an address) on one PC, the other shows a code,
// the person types it on the first and confirms on the second (PcPairing has the exchange). Only one pairing runs at a time (PairAttempts);
// a pairing closes pairing, a failed attempt leaves it open (so a PC that is not the person's cannot close it), except after too many wrong codes.
public sealed partial class OtherPcsService
{
    private long _pairingOpenUntil; // UTC ticks; read by the network tasks
    private readonly PairAttempts _attempts;
    private int _wrongCodes; // wrong codes since pairing was opened (UI thread)
    private TaskCompletionSource<string?>? _codeWait;
    private CancellationTokenSource? _openTimer;
    private volatile IReadOnlySet<string> _pairedIds = new HashSet<string>(); // the paired ids when pairing opened or a pair left, for the network tasks

    private void TakePairedIds() => _pairedIds = _settings.Pairs.Select(p => p.Id).ToHashSet();

    private bool IsPaired(string id) => _pairedIds.Contains(id);

    /// What the pairing panel shows.
    public PairingState Pairing { get; } = new();

    private bool PairingIsOpen => Interlocked.Read(ref _pairingOpenUntil) > DateTime.UtcNow.Ticks;

    /// A New PC tab is shown: this PC becomes visible to pairing for <see cref="Timing.PcPairingOpen"/> and lists the PCs that are open too; the tab <paramref name="tabId"/> shows what comes of it.
    public void OpenPairing(string tabId)
    {
        StartNetwork();
        if (Pairing.Phase is PairingPhase.AskingCode or PairingPhase.ShowingCode or PairingPhase.Working) return;
        TakePairedIds();
        Interlocked.Exchange(ref _pairingOpenUntil, (DateTime.UtcNow + Timing.PcPairingOpen).Ticks);
        _wrongCodes = 0;
        Pairing.TabId = tabId;
        Pairing.Message = string.Empty;
        Pairing.Phase = PairingPhase.Open;
        _discovery?.AnnounceNow();
        Tailscale.Check(string.Empty); // this PC's Tailscale address, so a PC pairing over the internet can be answered with it
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
            PcWork.Post(() =>
            {
                if (Pairing.Phase == PairingPhase.Open) ClosePairing();
            });
        });
        RaiseChanged();
    }

    /// Closes pairing and stops being visible to pairing; one running pairing is given up.
    public void ClosePairing()
    {
        Interlocked.Exchange(ref _pairingOpenUntil, 0);
        _openTimer?.Cancel();
        _attempts.GiveUp();
        _codeWait?.TrySetResult(null);
        _codeWait = null;
        Pairing.Code = string.Empty;
        Pairing.Phase = PairingPhase.Closed;
        _discovery?.AnnounceNow();
        StopNetworkIfIdle();
        RaiseChanged();
    }

    /// Asks a PC found on the network to pair; the code it shows is then typed here (<see cref="SubmitCode"/>).
    public void PairWith(NearbyPc pc) => PairWith(pc.Address, pc.Port, PcRoute.Lan);

    /// Asks the PC at an address (Tailscale, ZeroTier or any other network) to pair; it must have a New PC tab open.
    public void PairWith(string address)
    {
        if (!PcProtocol.TryParseAddress(address, out var host, out var port))
        {
            Pairing.Message = "That is not an address. Type a name or an address, such as 100.64.0.7.";
            return;
        }
        PairWith(host, port, PcRoute.Address);
    }

    /// Over the internet: asks the PC at a Tailscale name or address to pair; it must have a New PC tab open. Both PCs keep each other's Tailscale address, so either can dial afterwards.
    public void PairOverTailscale(string address)
    {
        if (!PcProtocol.TryParseAddress(address, out var host, out var port))
        {
            Pairing.Message = "That is not an address. Pick a PC from the list or type its Tailscale name or address, such as 100.64.0.7.";
            return;
        }
        var paired = _settings.Pairs.FirstOrDefault(p => p.TailscaleAddress == host || string.Equals(p.Name, host, StringComparison.OrdinalIgnoreCase));
        if (paired is not null)
        {
            // An already paired PC keeps its pairing: switching it to Tailscale only needs the checks of its own page.
            Pairing.Message = $"{paired.DisplayName} is already paired. Choose Connect: Tailscale on its page; it switches by itself.";
            CheckPairTailscale(paired.Id);
            RaiseChanged();
            return;
        }
        PairWith(host, port, PcRoute.Tailscale);
    }

    private void PairWith(string host, int port, PcRoute route)
    {
        if (Pairing.Phase is PairingPhase.AskingCode or PairingPhase.ShowingCode or PairingPhase.Working || _attempts.TryBegin() is not { } attempt) return;
        Pairing.Message = $"Connecting to {host}...";
        Pairing.Phase = PairingPhase.Working;
        RaiseChanged();
        TakePairedIds();
        var ownAddress = Tailscale.OwnAddress;
        _ = Guard.RunAsync("Other PCs: pairing", async () =>
        {
            var result = PairResult.Failed("Pairing did not finish. Nothing was paired.");
            try
            {
                using var client = new TcpClient();
                using (var connect = PcWire.Step(attempt.Stop)) await client.ConnectAsync(host, port, connect.Token).ConfigureAwait(false);
                client.NoDelay = true;
                result = await PcPairing.InitiateAsync(client.GetStream(), OwnId, _ownName, route == PcRoute.Tailscale ? ownAddress : string.Empty, IsPaired,
                    name => AskCodeAsync(attempt.Id, name, attempt.Stop), attempt.Stop).ConfigureAwait(false);
            }
            catch (Exception ex) when (PcWire.IsNetworkError(ex))
            {
                result = PairResult.Failed($"Couldn't reach a PC that is open to pairing at {host}. Open \"Pair a PC\" on it too.");
            }
            finally
            {
                PcWork.Post(() => FinishPairing(attempt.Id, result, route, host));
            }
        });
    }

    /// The 6 digits shown on the other PC, typed here (a space in them is fine); a wrong code gives this attempt up.
    public void SubmitCode(string code) => _codeWait?.TrySetResult(code.Trim());

    private async Task<string?> AskCodeAsync(long attempt, string peerName, CancellationToken stop)
    {
        var wait = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        PcWork.Post(() =>
        {
            if (!_attempts.Counts(attempt)) return;
            _codeWait = wait;
            Pairing.PeerName = peerName;
            Pairing.Message = string.Empty;
            Pairing.Phase = PairingPhase.AskingCode;
            RaiseChanged();
        });
        try
        {
            return await wait.Task.WaitAsync(Timing.PcPairingOpen, stop).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    // The part of a pairing the listening PC runs (the connection's first byte said "pairing"). A connection that ends before a code was shown
    // was not a pairing anyone here saw: nothing is said.
    private async Task RespondToPairingAsync(Stream stream)
    {
        if (!PairingIsOpen || _attempts.TryBegin() is not { } attempt) return; // not open, or busy: no answer at all
        var shown = false;
        var result = PairResult.Failed(null);
        try
        {
            result = await PcPairing.RespondAsync(stream, OwnId, _ownName, Tailscale.OwnAddress, IsPaired, (name, code) =>
            {
                shown = true;
                PcWork.Post(() => ShowCode(attempt.Id, name, code));
            }, ConfirmPairAsync, attempt.Stop).ConfigureAwait(false);
        }
        catch (Exception ex) when (PcWire.IsNetworkError(ex))
        {
            result = PairResult.Failed(shown ? "The other PC went away while pairing. Nothing was paired." : null);
        }
        finally
        {
            PcWork.Post(() => FinishPairing(attempt.Id, result, PcRoute.Lan, string.Empty));
        }
    }

    // The person at the PC that showed the code agrees too; the question closes by itself when the step's time runs out.
    private async Task<bool> ConfirmPairAsync(string peerName, CancellationToken dismiss)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PcWork.Post(() => _ = Guard.RunAsync("Other PCs: confirming a pairing", async () =>
        {
            try
            {
                answer.TrySetResult(await _ask(new DialogMessage($"Pair with {peerName}?", $"The code shown here was typed on {peerName}. Once paired, each PC can use what the other shares.", "Yes, pair", Dismiss: dismiss, YesNo: true)));
            }
            finally
            {
                answer.TrySetResult(false);
            }
        }));
        try
        {
            return await answer.Task.WaitAsync(dismiss).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private void ShowCode(long attempt, string peerName, string code)
    {
        if (!_attempts.Counts(attempt)) return;
        Pairing.PeerName = peerName;
        Pairing.Code = $"{code[..3]} {code[3..]}";
        Pairing.Phase = PairingPhase.ShowingCode;
        RaiseChanged();
    }

    // The address the other PC gave (Tailscale) wins and makes the route Tailscale on either side; otherwise a LAN pairing keeps no address.
    private void FinishPairing(long attempt, PairResult result, PcRoute route, string dialled)
    {
        if (!_attempts.End(attempt)) return; // given up (the panel was closed meanwhile): whatever came of it is dropped
        var tailscale = result.PeerAddress.Length > 0 ? result.PeerAddress : route == PcRoute.Tailscale ? dialled : string.Empty;
        var address = route == PcRoute.Address ? dialled : string.Empty;
        if (result.PeerAddress.Length > 0) route = PcRoute.Tailscale;
        _codeWait = null;
        Pairing.Code = string.Empty;
        if (Pairing.Phase == PairingPhase.Closed) return;
        if (result is { Paired: true, PeerId: { } id, PeerName: { } name, Secret: { } secret } && AddPair(id, name, secret, route, address, tailscale))
        {
            Interlocked.Exchange(ref _pairingOpenUntil, 0);
            TakePlace(Pairing.TabId, id);
            DropSetupTab(Pairing.TabId); // the New PC tab becomes the PC's own tab
            _report($"Paired with {name}. Choose what it shares on its tab; it may run this PC's power actions only once you allow it in its menu.");
            Pairing.Message = string.Empty;
            Pairing.Phase = PairingPhase.Ended;
        }
        else
        {
            if (result.WrongCode && ++_wrongCodes >= Timing.PcPairingWrongCodesMax)
            {
                Interlocked.Exchange(ref _pairingOpenUntil, 0);
                Pairing.Message = "Pairing was closed after several wrong codes. Open it again to pair.";
            }
            else if ((result.Problem ?? (result.Paired ? "Pairing did not finish. Nothing was paired." : null)) is { } problem)
                Pairing.Message = problem;
            Pairing.Phase = PairingIsOpen ? PairingPhase.Open : PairingPhase.Ended;
        }
        _discovery?.AnnounceNow();
        StopNetworkIfIdle();
        RaiseChanged();
    }

    private bool AddPair(string id, string name, byte[] secret, PcRoute route, string address, string tailscale)
    {
        if (id == OwnId || Find(id) is not null) return false; // a paired PC is paired again only after it was unpaired
        if (!_credentials.Write(CredentialTarget(id), Convert.ToBase64String(secret)))
        {
            _report("Windows would not keep the pairing key, so the PC was not paired.");
            return false;
        }
        _secrets[id] = secret;
        // A new pair may not run power actions here until the person allows it (pairs saved before keep what they had).
        var added = new PairedPc { Id = id, Name = name, Route = route, Address = address, TailscaleAddress = tailscale, MayRunPower = false };
        _settings.Pairs.Add(added);
        Wake.Register(added);
        _settings.TabOrder.Add(id);
        CloseLink(id);
        Save();
        StartNetwork();
        CheckLinksSoon();
        return true;
    }
}
