using Avalonia.Styling;

namespace OmniControl.App.Shared.Services;

/// THE way the app's light or dark theme is put in force (start-up, the Theme card, an import), so the App settings page need not exist for it.
public static class ThemeChoices
{
    public static void Apply(ThemeChoice choice)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = choice switch
            {
                ThemeChoice.Light => ThemeVariant.Light,
                ThemeChoice.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
    }
}
