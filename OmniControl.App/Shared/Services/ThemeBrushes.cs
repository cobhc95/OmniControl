using Avalonia.Styling;

namespace OmniControl.App.Shared.Services;

/// The colour brushes a service writes into the application's theme dictionaries (the accent's, the background's). Each brush is made once
/// and then only recoloured, so every DynamicResource that holds it follows a change at once. One instance per service.
internal sealed class ThemeBrushes
{
    private readonly Dictionary<(ThemeVariant, string), SolidColorBrush> _brushes = new();

    public void Set(Application app, ThemeVariant variant, string key, Color color)
    {
        if (_brushes.TryGetValue((variant, key), out var brush)) { brush.Color = color; return; }

        brush = new SolidColorBrush(color);
        _brushes[(variant, key)] = brush;
        DictionaryOf(app, variant)[key] = brush;
    }

    /// The application's own dictionary for <paramref name="variant"/> (looked up before the styles' dictionaries, so it wins).
    public static ResourceDictionary DictionaryOf(Application app, ThemeVariant variant)
    {
        if (!app.Resources.ThemeDictionaries.TryGetValue(variant, out var provider) || provider is not ResourceDictionary dict)
            app.Resources.ThemeDictionaries[variant] = dict = new ResourceDictionary();
        return dict;
    }
}
