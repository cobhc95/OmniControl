namespace OmniControl.App.Features.OtherPcs;

/// What another feature (the Smart Devices import) needs of the paired PCs (<see cref="OtherPcsService"/> is the one).
public interface IPairedPcs
{
    /// The paired PCs, in the order they were paired.
    IReadOnlyList<PairedPc> Pairs { get; }

    PairedPc? Find(string id);

    /// Raised when pairs, who is online, who is nearby or the pairing panel's state changed (UI thread).
    event Action? Changed;

    /// True when the paired PC is connected now.
    bool IsOnline(string id);

    /// What an offline PC's tag says: when it was last connected, or just "offline".
    string SeenWords(string id);

    /// The set-ups the PC last handed over (without scripts); null when it was never asked.
    string? KnownDeviceSetups(string pcId);

    /// Asks a connected PC for its device set-ups (<paramref name="includeScripts"/>: also the user's own script templates).
    Task<DeviceSetupsReply> RequestDeviceSetupsAsync(string pcId, bool includeScripts);
}
