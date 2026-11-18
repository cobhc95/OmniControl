using System.Text;
using OmniControl.App.Services;

namespace OmniControl.App.OtherPcs;

/// <summary>What a request for another PC's device set-ups came back with: the text of them, or one plain sentence saying why not.</summary>
public sealed record DeviceSetupsReply(string? Payload, string? Why);

// Handing device set-ups to another paired PC (docs/mockups/import-devices.html) and asking one for its set-ups. This part only carries the
// text: what is in it (and that nothing secret is) is the devices' own business (Devices/DeviceShare.cs). A request is answered only for a
// paired PC, over the encrypted link, and at most PcDevicesBurstMax times per window.
public sealed partial class OtherPcsService
{
    private readonly Dictionary<int, (string PcId, TaskCompletionSource<DeviceSetupsReply> Answer)> _devicePending = new();
    private readonly Dictionary<string, LaunchLimiter> _deviceLimits = new();

    /// <summary>The set-ups the PC last handed over (without scripts); null when it was never asked. Importable while the PC is offline.</summary>
    public string? KnownDeviceSetups(string pcId) => Find(pcId)?.DeviceSetups;

    /// <summary>Asks a connected PC for its device set-ups. <paramref name="includeScripts"/> also asks for the user's own script templates.</summary>
    public async Task<DeviceSetupsReply> RequestDeviceSetupsAsync(string pcId, bool includeScripts)
    {
        var name = Find(pcId)?.DisplayName ?? "That PC";
        if (!_links.TryGetValue(pcId, out var link)) return new DeviceSetupsReply(null, $"{name} is not connected.");
        var rid = ++_nextRid;
        var answer = new TaskCompletionSource<DeviceSetupsReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        _devicePending[rid] = (pcId, answer);
        var sent = await link.SendAsync(new PcMsg { T = PcMsgType.Devices, Rid = rid, Scripts = includeScripts });
        var reply = sent && await Task.WhenAny(answer.Task, Task.Delay(Timing.PcDevicesWait)) == answer.Task
            ? answer.Task.Result
            : new DeviceSetupsReply(null, $"{name} did not answer.");
        _devicePending.Remove(rid);
        if (reply.Payload is not null && !includeScripts && Find(pcId) is { } pair)
        {
            pair.DeviceSetups = reply.Payload;
            Save();
        }
        return reply;
    }

    private void HandleDevicesAnswer(PcLink link, PcMsg message)
    {
        if (message.Rid is not { } rid || !_devicePending.TryGetValue(rid, out var pending) || pending.PcId != link.PeerId) return;
        pending.Answer.TrySetResult(message.Ok == true && !string.IsNullOrEmpty(message.Payload)
            ? new DeviceSetupsReply(message.Payload, null)
            : new DeviceSetupsReply(null, Clip(message.Why, 200) is { Length: > 0 } why ? why : "It could not hand over its devices."));
    }

    private void HandleDevicesRequest(PcLink link, PcMsg message)
    {
        var reply = new PcMsg { T = PcMsgType.DevicesAnswer, Rid = message.Rid };
        if (Find(link.PeerId) is not { } pair) reply.Why = "This PC is not paired.";
        else if (!(_deviceLimits.TryGetValue(pair.Id, out var limit) ? limit : _deviceLimits[pair.Id] = new LaunchLimiter(Timing.PcDevicesBurstMax, Timing.PcDevicesBurstWindow)).TryAcquire())
            reply.Why = "Asked too often. Try again in a moment.";
        else
        {
            var text = string.Empty;
            if (Guard.Run("Other PCs: sharing devices", () => text = _shareDevices(message.Scripts == true)) is not null) reply.Why = "It could not read its devices.";
            else if (Encoding.UTF8.GetByteCount(text) > PcProtocol.MaxFrame - PcProtocol.MaxOpenFrame) reply.Why = "Its devices are too large to send.";
            else
            {
                reply.Ok = true;
                reply.Payload = text;
            }
        }
        _ = link.SendAsync(reply);
    }
}
