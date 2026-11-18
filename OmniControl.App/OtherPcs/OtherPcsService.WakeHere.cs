using OmniControl.App.Dialogs;
using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Wake;

namespace OmniControl.App.OtherPcs;

// The other side of the wake requests: what this PC does when a paired PC asks it to send a wake signal for a PC on its network, to read its
// wake facts again, to turn its wake settings on or to go to sleep as a test. Only a paired PC over the encrypted link may ask, at most
// Timing.WakeAskBurstMax times per window. A fix and a test change this PC, so each one shows this PC's own prompt first (and Windows asks
// for administrator rights once more for a fix): nothing changes unless a person here says yes.
public sealed partial class OtherPcsService
{
    private readonly Dictionary<string, LaunchLimiter> _wakeLimits = new();

    private void HandleWakeAsk(PcLink link, PcMsg message)
    {
        var reply = new PcMsg { T = PcMsgType.WakeAnswer, Rid = message.Rid };
        if (Find(link.PeerId) is not { } pair) reply.Why = "This PC is not paired.";
        else if (!(_wakeLimits.TryGetValue(pair.Id, out var limit) ? limit : _wakeLimits[pair.Id] = new LaunchLimiter(Timing.WakeAskBurstMax, Timing.WakeAskBurstWindow)).TryAcquire())
            reply.Why = "Asked too often. Try again in a moment.";
        else
        {
            _ = Guard.RunAsync("Other PCs: wake request", () => AnswerWakeAsync(link, pair, message, reply));
            return;
        }
        _ = link.SendAsync(reply);
    }

    private async Task AnswerWakeAsync(PcLink link, PairedPc pair, PcMsg message, PcMsg reply)
    {
        var sleepAfter = false;
        switch (message.Id)
        {
            case PcMsgType.WakeCheck:
                reply.Wake = await ReadOwnWakeAsync();
                reply.Ok = true;
                break;
            case PcMsgType.WakeRelay:
                await RelayAsync(message, reply);
                break;
            case PcMsgType.WakeFix:
                await FixHereAsync(pair, message, reply);
                break;
            case PcMsgType.WakeTest:
                sleepAfter = await TestHereAsync(pair, reply);
                break;
            default:
                reply.Why = "That is not something this PC does.";
                break;
        }
        await link.SendAsync(reply);
        if (!sleepAfter) return;
        await Task.Delay(Timing.WakeTestDelay); // the answer has left; the notice that this PC sleeps goes out with the sleep itself
        await Task.Run(() => Guard.Run("Other PCs: sleeping for the wake test", () => PowerCommands.Sleep()));
    }

    private async Task<WakeFacts> ReadOwnWakeAsync()
    {
        var facts = await Task.Run(WakeReader.Read);
        SetOwnWake(facts);
        return facts;
    }

    // A wake signal for a PC on this PC's own network (its network and address must be well formed, and the network must be this one).
    private async Task RelayAsync(PcMsg message, PcMsg reply)
    {
        var mac = Clip(message.Text, 17);
        var subnet = Clip(message.Payload, 20);
        var own = _ownWake ?? await ReadOwnWakeAsync();
        if (MagicPacket.ParseMac(mac) is null || !own.Known || own.Subnet != subnet)
        {
            reply.Why = "This PC is not on that network.";
            return;
        }
        var (ok, why) = await Task.Run(() =>
        {
            var sent = MagicPacket.Send(mac, subnet, out var reason);
            return (sent, reason);
        });
        reply.Ok = ok;
        reply.Why = why;
    }

    private async Task FixHereAsync(PairedPc pair, PcMsg message, PcMsg reply)
    {
        var fixes = (message.Text ?? string.Empty).Split(',').Take(4).Select(t => (Ok: WakeFixer.TryParse(t, out var fix), Fix: fix)).Where(x => x.Ok).Select(x => x.Fix).Distinct().ToList();
        if (fixes.Count == 0)
        {
            reply.Why = "There was nothing to turn on.";
            return;
        }
        var list = string.Join(", ", fixes.Select(FixWords));
        if (!await _askHere(new DialogMessage($"Set up wake for {pair.DisplayName}", $"{pair.DisplayName} asks to turn on: {list}. Windows will ask once more before it changes.", "Allow", YesNo: true)))
        {
            reply.Why = $"{_ownName} did not allow it.";
            return;
        }
        var (done, facts) = await ApplyWakeFixesHereAsync(fixes);
        reply.Wake = facts; // what really changed, row by row
        reply.Ok = done.Ok;
        if (!done.Ok) reply.Why = done.Why;
    }

    /// <summary>
    /// Turns the settings on for this PC, behind Windows' own administrator prompt: the one path for a fix another PC asked for (after this PC's own
    /// question) and for the one asked in this PC's own set-up window. Returns how it ended and the facts read again afterwards.
    /// </summary>
    private async Task<(WakeReply Reply, WakeFacts Facts)> ApplyWakeFixesHereAsync(IReadOnlyList<WakeFix> fixes)
    {
        var before = await ReadOwnWakeAsync();
        var result = await Task.Run(() => WakeFixer.Apply(fixes, before.Mac));
        var after = await ReadOwnWakeAsync();
        var ok = result.Failure == HelperFailure.None && result.ExitCode == 0;
        if (ok) return (new WakeReply(true, string.Empty), after);
        Guard.Log("Other PCs: wake fix", new IOException(result.Detail));
        var why = result.Failure switch
        {
            HelperFailure.Cancelled => $"The Windows prompt on {_ownName} was refused.",
            HelperFailure.Blocked => "Windows blocked the change.",
            HelperFailure.None => $"Windows could not change every setting on {_ownName}.",
            _ => $"The change could not be started on {_ownName}.",
        };
        return (new WakeReply(false, why), after);
    }

    private async Task<bool> TestHereAsync(PairedPc pair, PcMsg reply)
    {
        if (!await _askHere(new DialogMessage($"Wake test from {pair.DisplayName}", $"{pair.DisplayName} wants to put this PC to sleep, then wake it, to check that waking works. Save your work first.", "Sleep now", YesNo: true)))
        {
            reply.Why = $"{_ownName} did not allow it.";
            return false;
        }
        reply.Ok = true;
        return true;
    }

    private static string FixWords(WakeFix fix) => fix switch
    {
        WakeFix.MagicPacket => "Wake on Magic Packet",
        WakeFix.Arm => "allow the card to wake this PC",
        WakeFix.MagicOnly => "only a magic packet wakes it",
        _ => "Fast Startup off",
    };
}
