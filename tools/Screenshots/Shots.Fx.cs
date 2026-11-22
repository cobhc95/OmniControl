using Avalonia.Controls.Primitives;
using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Audio.Engine;
using OmniControl.App.Features.Volume.Fx;

namespace OmniControl.Screenshots;

internal static partial class Shots
{
    /// The FX window's larger response graph with the numbers and tags of a measured whole chain (an equaliser, an IR Loader and a made-up
    /// plug-in "Sample EQ"). The view-model is the real one over made-up slots and a made-up measurement: no engine, no plug-in is loaded and
    /// nothing is played (the window and its engine start Windows-facing work). The graph's own panel is taken out of its pop-up, which the
    /// headless frame would not carry.
    private static void FxMeasured(Camera camera)
    {
        var slots = new List<FxSlot>
        {
            new() { Kind = "eq", Name = "Equalizer", Bands = new List<EqBand>
            {
                new() { Id = 1, Freq = 80, Gain = 4, Q = 0.8, Type = EqType.LowShelf },
                new() { Id = 2, Freq = 3000, Gain = -3, Q = 1.2, Type = EqType.Peak },
                new() { Id = 3, Freq = 10000, Gain = 2, Q = 0.7, Type = EqType.HighShelf },
            } },
            new() { Kind = FxCatalog.IrKind, Name = "IR Loader" },
            new() { Kind = FxCatalog.PluginTypes[0].Kind, Name = "Sample EQ" },
        };
        using var analysis = new FxAnalysisViewModel(() => new RowLoad(LoadPlace.Own, 24, 0));
        analysis.Show(slots, FxEngines.Program, false, true);

        // The reference plus a gentle room dip, the plug-in's broad lift near 1 kHz and a roll-off above 12 kHz.
        var db = FxAnalysisViewModel.Freqs.Select((hz, i) =>
            analysis.Reference[i] - 1.5 * Bump(hz, 220, 0.5) + 2 * Bump(hz, 1000, 0.8) - 6 * Math.Max(0, Math.Log2(hz / 12000))).ToArray();
        var peak = Array.IndexOf(db, db.Max());
        analysis.ShowMeasured(new ChainMeasurement(db, db[peak], FxAnalysisViewModel.Freqs[peak], 12, 0.4, true, false, Array.Empty<string>()));

        var bar = new FxAnalysisBar { DataContext = analysis };
        var popup = bar.FindControl<Popup>("Enlarged")!;
        var panel = (Control)popup.Child!;
        popup.Child = null;
        panel.DataContext = analysis;
        panel.Width = 480;
        camera.Fit(new Border { Padding = new Thickness(12), Child = panel }, "fx-measured");
    }

    /// A bell on the log frequency axis: 1 at <paramref name="centre"/>, falling off over <paramref name="octaves"/>.
    private static double Bump(double hz, double centre, double octaves) => Math.Exp(-Math.Pow(Math.Log2(hz / centre) / octaves, 2));
}
