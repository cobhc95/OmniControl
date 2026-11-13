using OmniControl.App.Display;
using OmniControl.App.Hotkeys;
using OmniControl.App.Osd;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

/// <summary>
/// The software controls that belong to the whole desktop rather than one display: saturation (Windows' colour effect cannot be set per
/// display), and one Reset for every software change. Used on the UI thread.
/// </summary>
internal sealed class SoftwareGlobalItem
{
    private readonly SliderItem _saturation;
    private readonly OsdService _osd;

    public IReadOnlyList<IEditorItem> Cards { get; }
    public IReadOnlyList<HotkeyAction> Actions { get; }

    /// <param name="resetAll">Puts every display's picture and the saturation back (the page owns the displays).</param>
    public SoftwareGlobalItem(SoftwareDisplayService service, OsdService osd, string category, Action<string> report, Action resetAll)
    {
        _osd = osd;
        var spec = DisplayControls.SoftwareSaturation;
        _saturation = new SliderItem(spec.Label, spec.Default, v => service.SetSaturation(v), report, spec.Min, spec.Max, defaultValue: spec.Default) { Half = true };
        var reset = new ButtonItem("Reset everything", "Reset all", "Puts every display's picture, and the saturation, back to normal", resetAll) { Half = true };
        Cards = new IEditorItem[] { _saturation, reset };

        var actions = DisplayActions.Level(spec, DisplayControls.SoftwareKey(spec), category, DisplayControls.SoftwareGroup, (_, delta) => Step(spec, delta), devices: null).ToList();
        actions.Add(DisplayActions.Once("displays.sw.reset", "Reset all digital picture changes", "digital software reset normal",
            category, DisplayControls.SoftwareGroup, _ => resetAll(), devices: null));
        Actions = actions;
    }

    private void Step(LevelSpec spec, int delta)
    {
        _saturation.Value = Math.Clamp(_saturation.Value + delta, spec.Min, spec.Max);
        _osd.Show(spec.Osd, spec.OsdPercent(_saturation.Value));
    }

    public void Reset() => _saturation.Load(DisplayControls.SoftwareSaturation.Default);
}
