namespace OmniControl.App.Features.Settings.Pages;

// Appearance's Accent colour gallery (Windows accent, Windows' palette, then "Custom"; its pop-down's "Custom colour…" row opens the colour editor) and the Accent
// intensity card beside it; both apply live through AccentService and are kept in settings.json.
public sealed partial class AppSettingsPageViewModel
{
    private SliderItem _accentIntensity = null!;

    private static IReadOnlyList<string> AccentChoices => AccentService.Choices;

    private static int AccentIndex(string name) => Math.Max(0, AccentChoices.ToList().IndexOf(name));

    private GalleryItem MakeAccentCards(Action<string> report)
    {
        var choices = AccentChoices;
        var accent = new GalleryItem(AccentCard.Label, choices, AccentIndex(_settings.Accent),
            i => (new SolidColorBrush(AccentService.Resolve(choices[i])), AccentSwatch.Mark(choices[i])),
            i => UseAccent(choices[i]))
        {
            Tip = "Used everywhere; the OSD can match it. Windows accent follows the colour chosen in Windows; Custom colour… opens a colour editor.",
            Half = true,
            EditorIndex = AccentIndex(AccentService.CustomName),
            EditorText = AccentService.CustomEditorLabel,
            Editor = close => new AccentEditor
            {
                DataContext = new AccentEditorModel(AccentService.Custom, AccentService.Intensity, UseCustomAccent, UseAccentIntensity, close),
            },
        };
        _accentIntensity = new SliderItem(IntensityCard.Label, AccentService.Intensity, v =>
        {
            UseAccentIntensity(v);
            return OperationResult.Ok();
        }, report, maximum: AccentService.MaxIntensity, defaultValue: AccentService.DefaultIntensity)
        {
            Words = AccentService.IntensityWords,
            Half = true,
        };
        Follow(h => AccentService.Changed += h, h => AccentService.Changed -= h, accent.Refresh);
        Follow(h => AccentService.Changed += h, h => AccentService.Changed -= h, () => _accentIntensity.Load(AccentService.Intensity));
        return accent;
    }

    private void UseAccent(string name)
    {
        AccentService.Set(name);
        _settings.Accent = name;
        _settings.Save();
    }

    // The editor changes these many times a second while a handle is dragged, so the file is written shortly after the last change.
    private void UseCustomAccent(Color colour)
    {
        AccentService.SetCustom(colour);
        _settings.Accent = AccentService.CustomName;
        _settings.AccentCustom = ColorMath.Hex(colour);
        _settings.Save();
    }

    private void UseAccentIntensity(int intensity)
    {
        if (intensity == AccentService.Intensity && intensity == _settings.AccentIntensity) return;
        AccentService.SetIntensity(intensity);
        _settings.AccentIntensity = AccentService.Intensity;
        _settings.Save();
    }
}
