using OmniControl.App.Features.Hotkeys.Actions;
using OmniControl.App.Shared.Scripts;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Input.Mouse;

/// What buttons and middle-click gestures may start (<see cref="MouseActionCatalog.StartTextOf"/>): a mapping chosen on this PC is allowed
/// as it is chosen; one that came in by an import or a restore runs only after the person allows it at its first press.
public sealed partial class MouseService
{
    private readonly HashSet<string> _asking = new(); // start texts whose question is open (UI thread), so a held or repeated press asks once

    /// The programs, files and addresses the person allowed buttons and gestures to start on this PC.
    private CommandApprovals Approvals { get; }

    /// A file without approvals (a new install, or one saved before they existed): its mappings were the person's own, so they stay allowed.
    private void AllowMappingsOnce()
    {
        if (Settings.Approved is not null) return;
        Settings.Approved = new();
        foreach (var spec in Settings.Bindings.Values.Concat(Settings.ExtraActions.Values.SelectMany(e => e))) Allow(spec);
        Settings.Save(); // at once, so an import's file never finds this PC without its own approvals
    }

    private void Allow(string spec)
    {
        if (MouseActionCatalog.StartTextOf(MouseAction.Parse(spec)) is { } text) Approvals.Allow(text);
    }

    /// The person chose the input's actions on the Mouse and Trackpad page: stored, allowed and applied.
    public void SetActions(int layer, MouseButton button, IReadOnlyList<string> specs)
    {
        foreach (var spec in specs) Allow(spec);
        Settings.SetActions(layer, button, specs);
        Apply();
    }

    /// Asked by the runner on a worker: true when the action starts nothing of the person's choosing or is allowed. Otherwise the person is
    /// asked (a press is theirs) and, once allowed, the action runs.
    private bool MayStart(MouseAction action, MouseEvent at)
    {
        if (MouseActionCatalog.StartTextOf(action) is not { } text || Approvals.IsApproved(text)) return true;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_asking.Add(text)) return;
            Guard.Fire("Mouse: allowing what a button starts", async () =>
            {
                try
                {
                    if (await Approvals.AskAsync(text, new DialogMessage("Allow this button or gesture?",
                            "It starts this each time. Changing it later asks again.", "Allow", text, ShowDetails: true)))
                        Guard.OffUi("Mouse: running an allowed action", () => _runner.Run(action, at));
                }
                finally
                {
                    _asking.Remove(text);
                }
            }, _report);
        });
        return false;
    }
}
