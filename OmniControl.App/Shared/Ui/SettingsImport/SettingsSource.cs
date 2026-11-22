using OmniControl.App.Shared.Services.Backup;

namespace OmniControl.App.Shared.Ui.SettingsImport;

/// What the settings window lists: the source's name (in the title), the areas it may hold and which of them it has now.
public interface ISettingsSource
{
    string Name { get; }

    /// <see cref="ImportAreas.All"/> or <see cref="ImportAreas.Shared"/>: the window lists the ones the offers name, in this order.
    IReadOnlyList<ImportArea> Areas { get; }

    /// The areas it has, or the one plain sentence why they could not be read.
    Task<(IReadOnlyList<AreaOffer>? Offers, string? Why)> OffersAsync();
}

/// A source the window imports from (another PC, a backup file): it also hands over one area's files by file name.
public interface IImportSource : ISettingsSource
{
    Task<(IReadOnlyDictionary<string, string>? Files, string? Why)> AreaAsync(string area);
}
