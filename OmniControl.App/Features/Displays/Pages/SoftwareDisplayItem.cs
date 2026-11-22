namespace OmniControl.App.Features.Displays.Pages;

/// One display's software picture as the page's group of cards: a view of its <see cref="SoftwareDisplayModel"/>, made with the page.
internal sealed class SoftwareDisplayItem : IDisposable
{
    private readonly SoftwareDisplayModel _model;
    private readonly Dictionary<SoftwareLevel, SliderItem> _sliders = new();

    public string Name => _model.Name;
    public ItemGroup Group { get; }
    public IReadOnlyList<IEditorItem> Cards { get; }

    public SoftwareDisplayItem(SoftwareDisplayModel model, Action<string> report)
    {
        _model = model;
        foreach (var level in DisplayControls.Software)
        {
            var spec = level.Spec;
            _sliders[level] = new SliderItem(DisplayCards.Level(spec, DisplayControls.SoftwareGroup), model[level], v => model.Set(level, v), report, spec.Min, spec.Max, defaultValue: spec.Default) { Half = true };
        }
        Cards = DisplayControls.Software.Select(level => (IEditorItem)_sliders[level]).ToList();
        Group = new ItemGroup($"Displays/Software/{model.Id}", Name, Cards.Cast<object>().ToList())
        {
            Restore = model.RestoreDefaults,
            RestoreTip = $"Puts {Name}'s software picture back to normal",
        };
        model.Changed += Show;
        Show();
    }

    private void Show()
    {
        foreach (var (level, slider) in _sliders) CardMirror.Show(slider, _model[level], _model.Reason);
    }

    public void Dispose() => _model.Changed -= Show;
}
