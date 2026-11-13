using OmniControl.App.Audio;

namespace OmniControl.App.Fx;

/// <summary>
/// What the effect view-model needs from an equaliser's editor card, whichever kind it is (the Parametric's <see cref="EqPanelItem"/>,
/// a graphic one's <see cref="GraphicEqItem"/>), so the preset choice, saving and the Parameters list are built once for all of them.
/// </summary>
internal interface IEqEditor
{
    /// <summary>Shows saved bands without counting as an edit.</summary>
    void Load(IReadOnlyList<EqBand> bands);

    List<EqBand> Export();

    /// <summary>The user changed the bands: save them.</summary>
    event Action? Edited;

    /// <summary>The equaliser's overall gain (its own card), drawn into the curve.</summary>
    double OverallGainDb { get; set; }

    /// <summary>One card per band for the plain Parameters list; raises collection changes when bands come and go.</summary>
    IReadOnlyList<object> BandCards { get; }
}
