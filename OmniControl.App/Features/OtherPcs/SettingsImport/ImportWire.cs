using OmniControl.App.Shared.Services.Backup;

namespace OmniControl.App.Features.OtherPcs.SettingsImport;

/// What a request to another PC came back with: the text, or one plain sentence saying why not.
public sealed record ImportReply(string? Payload, string? Why);

/// How the import window reaches another paired PC. Implemented by the Other PCs service over its encrypted link; the import itself never
/// knows how the bytes travel.
public interface ISettingsTransport
{
    /// The paired PCs that are connected now: id and the name this PC calls them.
    IReadOnlyList<(string Id, string Name)> OnlinePcs();

    /// The name this PC calls the paired PC.
    string NameOf(string pcId);

    /// Whether the paired PC is connected now.
    bool IsOnline(string pcId);

    /// Asks the PC which areas it has (the payload is a json list of <see cref="AreaOffer"/>).
    Task<ImportReply> OffersAsync(string pcId);

    /// Asks the PC for the files of one area (the payload is a json map of file name to its text).
    Task<ImportReply> AreaAsync(string pcId, string area);
}
