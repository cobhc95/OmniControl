namespace OmniControl.App.Features.OtherPcs;

/// What the parts of Other PCs (the wake) read and change of the paired PCs and their links; <see cref="OtherPcsService"/> is the one.
internal interface IPcBook
{
    PairedPc? Find(string id);

    IReadOnlyList<PairedPc> Pairs { get; }

    bool IsOnline(string id);

    /// The live link to the PC (null: not connected).
    PcLink? LinkOf(string id);

    IReadOnlyList<PcLink> Links { get; }

    /// Saves the pairs after a part changed one.
    void Save();

    /// Tells the page that something it shows changed.
    void RaiseChanged();
}
