namespace OmniControl.App.SettingsImport;

/// <summary>What a PC says it has for one area: the area's name and, for lists, how many entries (null for plain settings).</summary>
public sealed record AreaOffer(string Area, int? Count);

/// <summary>What a request to another PC came back with: the text, or one plain sentence saying why not.</summary>
public sealed record ImportReply(string? Payload, string? Why);

/// <summary>
/// How the import window reaches another paired PC. Implemented by the Other PCs service over its encrypted link; the import itself never
/// knows how the bytes travel.
/// </summary>
public interface ISettingsTransport
{
    /// <summary>The paired PCs that are connected now: id and the name this PC calls them.</summary>
    IReadOnlyList<(string Id, string Name)> OnlinePcs();

    /// <summary>The name this PC calls the paired PC.</summary>
    string NameOf(string pcId);

    /// <summary>Whether the paired PC is connected now.</summary>
    bool IsOnline(string pcId);

    /// <summary>Asks the PC which areas it has (the payload is a json list of <see cref="AreaOffer"/>).</summary>
    Task<ImportReply> OffersAsync(string pcId);

    /// <summary>Asks the PC for the files of one area (the payload is a json map of file name to its text).</summary>
    Task<ImportReply> AreaAsync(string pcId, string area);
}
