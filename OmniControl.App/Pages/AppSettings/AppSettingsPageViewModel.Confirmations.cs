using OmniControl.App.Dialogs;

namespace OmniControl.App.Pages.AppSettingsPage;

/// <summary>
/// The Confirmations cards of "Your data": one card (its description says how many prompts are quiet; Reset all) and, after it, one card
/// per prompt the user ticked "Don't ask again" on, each with a Reset pill.
/// </summary>
public sealed partial class AppSettingsPageViewModel
{
    private const string ConfirmationKeywords = "confirm confirmation ask again don't dont prompt question reset";

    private readonly ConfirmationMemory _confirmations;
    private readonly ButtonItem _resetAllConfirmations;
    private readonly List<ButtonItem> _confirmationRows = new();

    /// <summary>Rebuilds the per-prompt cards after the one that heads them; runs when a prompt is remembered or reset.</summary>
    private void ReloadConfirmations()
    {
        foreach (var row in _confirmationRows) DataItems.Remove(row);
        _confirmationRows.Clear();
        var at = DataItems.IndexOf(_resetAllConfirmations) + 1;
        foreach (var (key, name) in _confirmations.Items)
        {
            var row = new ButtonItem(name, "Reset", "Asks again before this", () => _confirmations.Reset(key));
            _confirmationRows.Add(row);
            DataItems.Insert(at++, row);
        }
        _resetAllConfirmations.Description = _confirmationRows.Count == 0
            ? "No prompt is set to stay quiet. Tick Don't ask again on a confirmation to list it here."
            : $"{_confirmationRows.Count} not asked";
    }

    private IEnumerable<SearchEntry> ConfirmationSearchEntries() => _confirmationRows
        .Select(row => Entry(row, DataName, ConfirmationKeywords))
        .Prepend(Entry(_resetAllConfirmations, DataName, ConfirmationKeywords + " all"));
}
