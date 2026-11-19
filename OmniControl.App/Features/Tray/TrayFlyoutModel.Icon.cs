using OmniControl.App.Features.Icons;

namespace OmniControl.App.Features.Tray;

// The tray icon's look, kept with the panel in tray.json.
public sealed partial class TrayFlyoutModel : ITrayIconLook
{
    public string? TrayIcon => _settings.Icon;

    public string? TrayIconColour => _settings.IconColour;

    public event Action? TrayIconChanged;

    public void SetTrayIcon(string? icon)
    {
        if (icon == _settings.Icon) return;
        _settings.Icon = icon;
        SaveLook();
    }

    public void SetTrayIconColour(string? colour)
    {
        if (colour == _settings.IconColour) return;
        _settings.IconColour = colour;
        SaveLook();
    }

    public async Task ChooseTrayIconAsync(Visual from)
    {
        var request = new IconRequest(TrayIconChoices.IconWords, _settings.Icon, TrayIconChoices.Icons.Select(c => c.Icon).OfType<string>().ToList());
        if (await _icons.ChooseAsync(from, request) is { } pick) SetTrayIcon(pick.Value);
    }

    private void SaveLook()
    {
        _settings.Save();
        TrayIconChanged?.Invoke();
    }

    /// A user icon in use as the tray icon may have been removed: the tray draws again (a library icon cannot change).
    private void OnUserIconsChanged()
    {
        if (_settings.Icon is { } icon && IconLibrary.Find(icon) is null) TrayIconChanged?.Invoke();
    }
}
