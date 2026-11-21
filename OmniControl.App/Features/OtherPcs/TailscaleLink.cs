using OmniControl.Core.Engine.Tailscale;

namespace OmniControl.App.Features.OtherPcs;

/// Tailscale as the transport of Other PCs: reads Tailscale's state for the checklist of pairing over the internet, keeps the last state read
/// (this PC's own Tailscale address, the tailnet's PCs) and runs the fix a person presses. Everything it reads is read-only; nothing changes
/// until a fix is pressed. UI thread; the reading runs on a worker.
public sealed class TailscaleLink
{
    private const string DownloadUrl = "https://tailscale.com/download", AddDeviceUrl = "https://tailscale.com/kb/1017/install";

    private readonly Action<string> _report;
    private readonly Action _changed;
    private readonly CancellationToken _stop;
    private TailscaleStatus _status = TailscaleStatus.NotInstalled;
    private bool _busy;
    private string? _again;

    /// <param name="changed">Tells the page that the checklist or the state read changed.</param>
    /// <param name="stop">Ends the waits when Other PCs shuts down; nothing new starts after it.</param>
    internal TailscaleLink(Action<string> report, Action changed, CancellationToken stop)
    {
        _report = report;
        _changed = changed;
        _stop = stop;
    }

    /// The checklist as last read (empty until the first read).
    public IReadOnlyList<TailscaleCheck> Checklist { get; private set; } = Array.Empty<TailscaleCheck>();

    /// The tailnet's Windows PCs, online ones first.
    public IReadOnlyList<TailscalePeer> Peers => _status.Peers;

    /// True while Tailscale is being read.
    public bool Busy => _busy;

    /// This PC's Tailscale address when it is connected, else empty.
    internal string OwnAddress => _status.Connected ? _status.OwnAddress : string.Empty;

    /// Keeps a state another check read (a paired PC's checks), so the address and the peers stay current.
    internal void Took(TailscaleStatus status) => _status = status;

    /// Reads Tailscale, the firewall rule and (when <paramref name="target"/> is given) whether that PC answers; the result arrives through the change callback.
    public void Check(string target)
    {
        if (_stop.IsCancellationRequested) return;
        if (_busy)
        {
            _again = target; // the read in progress is older than this ask: read once more when it ends
            return;
        }
        _busy = true;
        _again = null;
        _changed();
        Guard.OffUi("Other PCs: Tailscale checks", async () =>
        {
            TailscaleStatus status = TailscaleStatus.NotInstalled;
            bool? firewall = null, reach = null;
            try
            {
                status = TailscaleStatusReader.Read();
                firewall = status.Installed ? TailscaleFirewall.IsAllowed() : null;
                reach = firewall == true && status.Connected ? await ReachAsync(target).ConfigureAwait(false) : null;
            }
            finally
            {
                var (read, fw, answered) = (status, firewall, reach);
                PcWork.Post(() =>
                {
                    _status = read;
                    Checklist = TailscaleChecks.Build(read, fw, answered, target);
                    _busy = false;
                    _changed();
                    if (_again is { } again) Check(again);
                });
            }
        });
    }

    private async Task<bool?> ReachAsync(string target)
    {
        if (!PcProtocol.TryParseAddress(target, out var host, out var port)) return null;
        return await TcpReach.CanConnectAsync(host, port, _stop).ConfigureAwait(false);
    }

    /// The fix a failing check offers (the person pressed it): opens a help page, starts or signs in Tailscale, or asks Windows' administrator prompt for the firewall rule; then <paramref name="recheck"/> reads everything again (the pairing panel's or one PC's).
    public void RunFix(string checkId, Action recheck)
    {
        if (_stop.IsCancellationRequested) return;
        switch (checkId)
        {
            case TailscaleChecks.Installed:
                if (WebPage.Open(DownloadUrl) is { } problem) _report(problem);
                return;
            case TailscaleChecks.Peer:
                if (WebPage.Open(AddDeviceUrl) is { } peerProblem) _report(peerProblem);
                return;
            case TailscaleChecks.Running:
                Start("Other PCs: starting Tailscale", () => TailscaleCli.Start(_status.Answered), "Tailscale could not be started. Open it from the Start menu.", recheck);
                return;
            case TailscaleChecks.SignedIn:
                Start("Other PCs: Tailscale sign-in", TailscaleCli.SignIn, "Tailscale's sign-in could not be opened. Open Tailscale from the Start menu and sign in there.", recheck);
                return;
            case TailscaleChecks.Address:
                Guard.OffUi("Other PCs: restarting Tailscale", async () =>
                {
                    var result = TailscaleAdapter.Restart();
                    if (result.Failure == HelperFailure.None && result.ExitCode == 0) await Task.Delay(Timing.PcTailscaleAfterFix, _stop).ConfigureAwait(false); // the adapter needs a moment to get its address
                    PcWork.Post(() =>
                    {
                        if (result.Failure == HelperFailure.Cancelled) _report("Windows' permission prompt was closed, so Tailscale was not restarted.");
                        else if (result.Failure != HelperFailure.None || result.ExitCode != 0)
                        {
                            Guard.Log("Other PCs: restarting Tailscale", new IOException(result.Detail));
                            _report("Tailscale could not be restarted. Restart it from Windows' Services instead.");
                        }
                        recheck();
                    });
                });
                return;
            case TailscaleChecks.Firewall:
                Guard.OffUi("Other PCs: firewall rule", () =>
                {
                    var result = TailscaleFirewall.Allow(PcProtocol.TcpPort);
                    PcWork.Post(() =>
                    {
                        if (result.Failure == HelperFailure.Cancelled) _report("Windows' permission prompt was closed, so the firewall was not changed.");
                        else if (result.Failure != HelperFailure.None || result.ExitCode != 0)
                        {
                            Guard.Log("Other PCs: firewall rule", new IOException(result.Detail));
                            _report("Windows Firewall could not be changed. Allow OmniControl on Tailscale in Windows Security instead.");
                        }
                        recheck();
                    });
                    return Task.CompletedTask;
                });
                return;
        }
    }

    private void Start(string what, Func<bool> start, string failed, Action recheck)
    {
        Guard.OffUi(what, async () =>
        {
            var started = start();
            if (!started) _report(failed);
            await Task.Delay(Timing.PcTailscaleAfterFix, _stop).ConfigureAwait(false); // Tailscale needs a moment to come up or sign in
            PcWork.Post(recheck);
        });
    }
}
