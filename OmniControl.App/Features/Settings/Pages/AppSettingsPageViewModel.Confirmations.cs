namespace OmniControl.App.Features.Settings.Pages;

/// The Confirmations cards of "Your data": one card (its description says how many prompts are quiet; Reset all) and, after it, one card
/// per prompt the user ticked "Don't ask again" on, each with a Reset pill.
public sealed partial class AppSettingsPageViewModel
{
    private readonly ConfirmationMemory _confirmations;
    private readonly ButtonItem _resetAllConfirmations;
    private readonly List<ButtonItem> _confirmationRows = new();

    /// Rebuilds the per-prompt cards after the one that heads them; runs when a prompt is remembered or reset.
    private void ReloadConfirmations()
    {
        foreach (var row in _confirmationRows) DataItems.Remove(row);
        _confirmationRows.Clear();
        var at = DataItems.IndexOf(_resetAllConfirmations) + 1;
        foreach (var (key, name) in _confirmations.Items)
        {
            var row = new ButtonItem(ConfirmationCard(name), "Reset", () => _confirmations.Reset(key));
            _confirmationRows.Add(row);
            DataItems.Insert(at++, row);
        }
        _resetAllConfirmations.Description = _confirmationRows.Count == 0
            ? "No prompt is set to stay quiet. Tick Don't ask again on a confirmation to list it here."
            : $"{_confirmationRows.Count} not asked";
    }
}
