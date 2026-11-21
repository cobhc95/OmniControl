using System.Text;
using System.Text.Json;
using OmniControl.App.Features.OtherPcs.SettingsImport;

namespace OmniControl.App.Features.OtherPcs;

// Handing this PC's settings areas to another paired PC that imports them, and asking one for its areas.
// This part only carries the text: which areas exist, what is left out (machine-specific stores, approvals, passwords) and how the files are
// applied is the settings import's business (SettingsImport/). A request is answered only for a paired PC, over the encrypted link, and
// at most PcImportBurstMax times per window.
public sealed partial class OtherPcsService : ISettingsTransport
{
    private readonly PcLimits _importLimits = new(Timing.PcImportBurstMax, Timing.PcImportBurstWindow);

    public IReadOnlyList<(string Id, string Name)> OnlinePcs() => Pairs.Where(p => IsOnline(p.Id)).Select(p => (p.Id, p.DisplayName)).ToList();

    public string NameOf(string pcId) => Find(pcId)?.DisplayName ?? "That PC";

    public Task<ImportReply> OffersAsync(string pcId) => AskImportAsync(pcId, new PcMsg { T = PcMsgType.ImportList });

    public Task<ImportReply> AreaAsync(string pcId, string area) => AskImportAsync(pcId, new PcMsg { T = PcMsgType.ImportGet, Text = area });

    private async Task<ImportReply> AskImportAsync(string pcId, PcMsg request)
    {
        var name = NameOf(pcId);
        if (!_links.TryGetValue(pcId, out var link)) return new ImportReply(null, $"{name} is not connected.");
        return await _requests.AskAsync(link, request, Timing.PcImportWait) is not { } message ? new ImportReply(null, $"{name} did not answer.")
            : message.Ok == true && !string.IsNullOrEmpty(message.Payload) ? new ImportReply(message.Payload, null)
            : new ImportReply(null, PcProtocol.Clip(message.Why, 200) is { Length: > 0 } why ? why : "It could not hand over its settings.");
    }

    private void HandleImportRequest(PcLink link, PcMsg message)
    {
        var reply = new PcMsg { T = PcMsgType.ImportAnswer, Rid = message.Rid };
        if (Find(link.PeerId) is not { } pair) reply.Why = "This PC is not paired.";
        else if (!pair.MayReadSettings) reply.Why = RefuseReading(pair, "settings");
        else if (!_importLimits.TryAcquire(pair.Id))
            reply.Why = "Asked too often. Try again in a moment.";
        else
        {
            var text = string.Empty;
            var known = true;
            if (Guard.Run("Other PCs: sharing settings", () =>
                {
                    if (message.T == PcMsgType.ImportList) text = JsonSerializer.Serialize(_settingsShare.Offer(), PcProtocol.Json);
                    else if (_settingsShare.Pack(message.Text ?? string.Empty) is { } files) text = JsonSerializer.Serialize(files, PcProtocol.Json);
                    else known = false;
                }) is not null) reply.Why = "It could not read its settings.";
            else if (!known) reply.Why = "It has no such area.";
            else if (Encoding.UTF8.GetByteCount(text) > PcProtocol.MaxFrame - PcProtocol.MaxOpenFrame) reply.Why = "That area is too large to send.";
            else
            {
                reply.Ok = true;
                reply.Payload = text;
                _report(message.T == PcMsgType.ImportList ? $"{pair.DisplayName} is reading this PC's settings to import them."
                    : $"{pair.DisplayName} read this PC's settings: {PcProtocol.Clip(message.Text, 40)}.");
            }
        }
        _ = link.SendAsync(reply);
    }

    // A PC not allowed to read is refused, and the person here is told it asked; the sentence goes back to that PC.
    private string RefuseReading(PairedPc pair, string what)
    {
        _report($"{pair.DisplayName} asked to read this PC's {what}; it is not allowed (its ⋯ menu, Allowed here).");
        return $"{_ownName} doesn't allow reading its {what}. Allow it on {_ownName}: this PC's ⋯ menu, Allowed here.";
    }
}
