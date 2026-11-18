using System.Text;
using System.Text.Json;
using OmniControl.App.Services;
using OmniControl.App.SettingsImport;

namespace OmniControl.App.OtherPcs;

// Handing this PC's settings areas to another paired PC that imports them, and asking one for its areas (docs/mockups/import-settings-from-pc.html).
// This part only carries the text: which areas exist, what is left out (machine-specific stores, approvals, passwords) and how the files are
// applied is the settings import's business (SettingsImport/). A request is answered only for a paired PC, over the encrypted link, and
// at most PcImportBurstMax times per window.
public sealed partial class OtherPcsService : ISettingsTransport
{
    private readonly Dictionary<int, (string PcId, TaskCompletionSource<ImportReply> Answer)> _importPending = new();
    private readonly Dictionary<string, LaunchLimiter> _importLimits = new();

    public IReadOnlyList<(string Id, string Name)> OnlinePcs() => Pairs.Where(p => IsOnline(p.Id)).Select(p => (p.Id, p.DisplayName)).ToList();

    public string NameOf(string pcId) => Find(pcId)?.DisplayName ?? "That PC";

    public Task<ImportReply> OffersAsync(string pcId) => AskImportAsync(pcId, new PcMsg { T = PcMsgType.ImportList });

    public Task<ImportReply> AreaAsync(string pcId, string area) => AskImportAsync(pcId, new PcMsg { T = PcMsgType.ImportGet, Text = area });

    private async Task<ImportReply> AskImportAsync(string pcId, PcMsg request)
    {
        var name = NameOf(pcId);
        if (!_links.TryGetValue(pcId, out var link)) return new ImportReply(null, $"{name} is not connected.");
        var rid = ++_nextRid;
        request.Rid = rid;
        var answer = new TaskCompletionSource<ImportReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        _importPending[rid] = (pcId, answer);
        var sent = await link.SendAsync(request);
        var reply = sent && await Task.WhenAny(answer.Task, Task.Delay(Timing.PcImportWait)) == answer.Task
            ? answer.Task.Result
            : new ImportReply(null, $"{name} did not answer.");
        _importPending.Remove(rid);
        return reply;
    }

    private void HandleImportAnswer(PcLink link, PcMsg message)
    {
        if (message.Rid is not { } rid || !_importPending.TryGetValue(rid, out var pending) || pending.PcId != link.PeerId) return;
        pending.Answer.TrySetResult(message.Ok == true && !string.IsNullOrEmpty(message.Payload)
            ? new ImportReply(message.Payload, null)
            : new ImportReply(null, Clip(message.Why, 200) is { Length: > 0 } why ? why : "It could not hand over its settings."));
    }

    private void HandleImportRequest(PcLink link, PcMsg message)
    {
        var reply = new PcMsg { T = PcMsgType.ImportAnswer, Rid = message.Rid };
        if (Find(link.PeerId) is not { } pair) reply.Why = "This PC is not paired.";
        else if (!(_importLimits.TryGetValue(pair.Id, out var limit) ? limit : _importLimits[pair.Id] = new LaunchLimiter(Timing.PcImportBurstMax, Timing.PcImportBurstWindow)).TryAcquire())
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
                if (message.T == PcMsgType.ImportList) _report($"{pair.DisplayName} is reading this PC's settings to import them.");
            }
        }
        _ = link.SendAsync(reply);
    }
}
