using OmniControl.App.Features.Volume.Audio;

namespace OmniControl.App.Features.Volume.Fx;

/// What the effect view-model needs from an equaliser's editor card, whichever kind it is (the Parametric's <see cref="EqPanelItem"/>,
/// a graphic one's <see cref="GraphicEqItem"/>), so the preset choice, saving and the Parameters list are built once for all of them.
internal interface IEqEditor
{
    /// Shows saved bands without counting as an edit.
    void Load(IReadOnlyList<EqBand> bands);

    List<EqBand> Export();

    /// The user changed the bands: save them.
    event Action? Edited;

    /// The equaliser's overall gain (its own card), drawn into the curve.
    double OverallGainDb { get; set; }

    /// One card per band for the plain Parameters list; raises collection changes when bands come and go.
    IReadOnlyList<object> BandCards { get; }
}
