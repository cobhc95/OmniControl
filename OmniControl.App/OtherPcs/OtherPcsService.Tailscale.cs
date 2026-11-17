using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Tailscale;

namespace OmniControl.App.OtherPcs;

// Over the internet (docs/mockups/other-pcs-tailscale.html): Tailscale is only the transport, so this part reads Tailscale's state for the
// checklist, runs the fix a person presses, and re-checks a Tailscale-paired PC that went offline. Everything it reads is read-only;
// nothing changes until a fix is pressed. The pairing itself is the same exchange as on the network (PcPairing).
public sealed partial class OtherPcsService
{
    private const string TailscaleDownloadUrl = "https://tailscale.com/download", TailscaleAddDeviceUrl = "https://tailscale.com/kb/1017/install";

    private TailscaleStatus _tsStatus = TailscaleStatus.NotInstalled;
    private bool _tsBusy;
    private string? _tsAgain;

    /// <summary>The checklist as last read (empty until the first read).</summary>
    public IReadOnlyList<TailscaleCheck> TailscaleChecklist { get; private set; } = Array.Empty<TailscaleCheck>();

    /// <summary>The tailnet's Windows PCs, online ones first.</summary>
    public IReadOnlyList<TailscalePeer> TailscalePeers => _tsStatus.Peers;

    /// <summary>True while Tailscale is being read.</summary>
    public bool TailscaleBusy => _tsBusy;

    /// <summary>This PC's Tailscale address when it is connected, else empty.</summary>
    private string OwnTailscaleAddress => _tsStatus.Connected ? _tsStatus.OwnAddress : string.Empty;

    /// <summary>Reads Tailscale, the firewall rule and (when <paramref name="target"/> is given) whether that PC answers; the result arrives through <see cref="Changed"/>.</summary>
    public void CheckTailscale(string target)
    {
        if (StartupMode.Inert || _disposed) return;
        if (_tsBusy)
        {
            _tsAgain = target; // the read in progress is older than this ask: read once more when it ends
            return;
        }
        _tsBusy = true;
        _tsAgain = null;
        RaiseChanged();
        OffUi("Other PCs: Tailscale checks", async () =>
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
                Post(() =>
                {
                    _tsStatus = read;
                    TailscaleChecklist = TailscaleChecks.Build(read, fw, answered, target);
                    _tsBusy = false;
                    RaiseChanged();
                    if (_tsAgain is { } again) CheckTailscale(again);
                });
            }
        });
    }

    // Reading Tailscale, netsh and the administrator prompt all wait on other programs, so they never run on the UI thread.
    private static void OffUi(string what, Func<Task> work) => _ = Task.Run(() => Guard.RunAsync(what, work));

    private async Task<bool?> ReachAsync(string target)
    {
        if (!TryParseAddress(target, out var host, out var port)) return null;
        return await TcpReach.CanConnectAsync(host, port, _stop.Token).ConfigureAwait(false);
    }

    /// <summary>The fix a failing check offers (the person pressed it): opens a help page, starts or signs in Tailscale, or asks Windows' administrator prompt for the firewall rule; then <paramref name="recheck"/> reads everything again (the pairing panel's or one PC's).</summary>
    public void RunTailscaleFix(string checkId, Action recheck)
    {
        if (StartupMode.Inert || _disposed) return;
        switch (checkId)
        {
            case TailscaleChecks.Installed:
                if (WebPage.Open(TailscaleDownloadUrl) is { } problem) _report(problem);
                return;
            case TailscaleChecks.Peer:
                if (WebPage.Open(TailscaleAddDeviceUrl) is { } peerProblem) _report(peerProblem);
                return;
            case TailscaleChecks.Running:
                RunFix("Other PCs: starting Tailscale", () => TailscaleCli.Start(_tsStatus.Answered), "Tailscale could not be started. Open it from the Start menu.", recheck);
                return;
            case TailscaleChecks.SignedIn:
                RunFix("Other PCs: Tailscale sign-in", TailscaleCli.SignIn, "Tailscale's sign-in could not be opened. Open Tailscale from the Start menu and sign in there.", recheck);
                return;
            case TailscaleChecks.Firewall:
                OffUi("Other PCs: firewall rule", () =>
                {
                    var result = TailscaleFirewall.Allow(PcProtocol.TcpPort);
                    Post(() =>
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

    private void RunFix(string what, Func<bool> start, string failed, Action recheck)
    {
        OffUi(what, async () =>
        {
            var started = start();
            if (!started) Post(() => _report(failed));
            await Task.Delay(Timing.PcTailscaleAfterFix, _stop.Token).ConfigureAwait(false); // Tailscale needs a moment to come up or sign in
            Post(recheck);
        });
    }
}
