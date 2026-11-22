using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Shared.Ui.Pages;

/// THE header preset drop-down of every preset group registered in the master list: the choice is made from the group's own
/// <see cref="IPresetProvider"/> (its entries, the one in use, applying, "changed"), so no page re-derives names, the shown preset or what
/// a pick does. The shown name is always the entry in use, never remembered; a group whose live settings can match none of its presets
/// (the Displays groups) starts the list with <see cref="CustomText"/>, which applies nothing. A provider that cannot save anything now
/// (<see cref="IPresetProvider.AddUnavailableReason"/>) greys the drop-down with the reason as its tooltip. "Manage presets…" is
/// <paramref name="manage"/>, which the chip menu runs through <see cref="ChoiceItem.Manage"/>.
internal sealed class ProviderChoice : IDisposable
{
    public const string CustomText = "Custom", NoneText = "No presets", ManageText = "Manage presets\u2026";

    private readonly IPresetProvider _provider;
    private readonly bool _withCustom;

    /// <param name="label">What search results call the drop-down.</param>
    /// <param name="withCustom">True: the list starts with "Custom", shown while no preset is in use.</param>
    public ProviderChoice(string label, IPresetProvider provider, Action manage, bool withCustom = false)
    {
        _provider = provider;
        _withCustom = withCustom;
        Choice = new ChoiceItem(label, new[] { string.Empty }, string.Empty, OnPicked) { Manage = manage };
        provider.Changed += Refresh;
        Refresh();
    }

    /// The bare drop-down (its label is what search results show).
    public ChoiceItem Choice { get; }

    /// Shows the list and the name for the presets and settings as they are now.
    public void Refresh()
    {
        if (_provider.AddUnavailableReason is { } reason)
        {
            Choice.Load(new[] { NoneText }, NoneText); // the reason goes in the tooltip, so the picker keeps its normal size
            Choice.Availability.MakeUnavailable(reason);
            return;
        }
        Choice.Availability.MakeAvailable();
        var entries = _provider.Entries;
        var names = entries.Select(e => e.Name);
        Choice.Load((_withCustom ? names.Prepend(CustomText) : names).ToList(),
            entries.FirstOrDefault(e => e.InUse)?.Name ?? (_withCustom ? CustomText : string.Empty));
    }

    private void OnPicked(string name)
    {
        if (_provider.Entries.FirstOrDefault(e => PresetRules.Same(e.Name, name)) is { } entry) _provider.Apply(entry.Id);
        Refresh(); // "Custom" applies nothing: the box goes back to what is true
    }

    public void Dispose() => _provider.Changed -= Refresh;
}
