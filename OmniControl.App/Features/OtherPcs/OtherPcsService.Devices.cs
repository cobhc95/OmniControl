using System.Text;

namespace OmniControl.App.Features.OtherPcs;

/// What a request for another PC's device set-ups came back with: the text of them, or one plain sentence saying why not.
public sealed record DeviceSetupsReply(string? Payload, string? Why);

// Handing device set-ups to another paired PC and asking one for its set-ups. This part only carries the
// text: what is in it (and that nothing secret is) is the devices' own business (Devices/DeviceShare.cs). A request is answered only for a
// paired PC, over the encrypted link, and at most PcDevicesBurstMax times per window.
public sealed partial class OtherPcsService
{
    private readonly PcLimits _deviceLimits = new(Timing.PcDevicesBurstMax, Timing.PcDevicesBurstWindow);

    /// The set-ups the PC last handed over (without scripts); null when it was never asked. Importable while the PC is offline.
    public string? KnownDeviceSetups(string pcId) => Find(pcId)?.DeviceSetups;

    /// Asks a connected PC for its device set-ups. <paramref name="includeScripts"/> also asks for the user's own script templates.
    public async Task<DeviceSetupsReply> RequestDeviceSetupsAsync(string pcId, bool includeScripts)
    {
        var name = Find(pcId)?.DisplayName ?? "That PC";
        if (!_links.TryGetValue(pcId, out var link)) return new DeviceSetupsReply(null, $"{name} is not connected.");
        var reply = await _requests.AskAsync(link, new PcMsg { T = PcMsgType.Devices, Scripts = includeScripts }, Timing.PcDevicesWait) is { } message
            ? message.Ok == true && !string.IsNullOrEmpty(message.Payload)
                ? new DeviceSetupsReply(message.Payload, null)
                : new DeviceSetupsReply(null, PcProtocol.Clip(message.Why, 200) is { Length: > 0 } why ? why : "It could not hand over its devices.")
            : new DeviceSetupsReply(null, $"{name} did not answer.");
        if (reply.Payload is not null && !includeScripts && Find(pcId) is { } pair)
        {
            pair.DeviceSetups = reply.Payload;
            Save();
        }
        return reply;
    }

    private void HandleDevicesRequest(PcLink link, PcMsg message)
    {
        var reply = new PcMsg { T = PcMsgType.DevicesAnswer, Rid = message.Rid };
        if (Find(link.PeerId) is not { } pair) reply.Why = "This PC is not paired.";
        else if (!pair.MayReadSettings) reply.Why = RefuseReading(pair, "devices");
        else if (!_deviceLimits.TryAcquire(pair.Id))
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
                _report($"{pair.DisplayName} read this PC's device set-ups{(message.Scripts == true ? " and script templates" : string.Empty)}.");
            }
        }
        _ = link.SendAsync(reply);
    }
}
