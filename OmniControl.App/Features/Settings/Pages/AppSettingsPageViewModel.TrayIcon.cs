using OmniControl.App.Features.Tray;

namespace OmniControl.App.Features.Settings.Pages;

// Appearance's Tray icon and Tray icon colour cards: the same choices as the tray menu's two sub-menus, read from and set through the tray.
public sealed partial class AppSettingsPageViewModel
{
    private MenuChoiceItem _trayIcon = null!;
    private GalleryItem _trayColour = null!;
    private SliderItem _trayGrace = null!, _trayStagger = null!, _trayGap = null!;
    private ITrayIconLook _trayLook = null!;

    /// The tray icon's look now, and by default, for Restore defaults.
    private (string? Icon, string? Colour) TrayLook => (_trayLook.TrayIcon, _trayLook.TrayIconColour);
    private static readonly (string? Icon, string? Colour) DefaultTrayLook = (TraySettings.DefaultIcon, TraySettings.DefaultIconColour);

    /// The default look in words for the restore question.
    private static string DefaultTrayWords() =>
        $"{TrayIconChoices.IconWords} \"{TrayIconChoices.NameOf(DefaultTrayLook.Icon)}\" in \"{TrayIconChoices.Colours.First(c => c.Colour == DefaultTrayLook.Colour).Name}\", ";

    private void SetTrayLook((string? Icon, string? Colour) look)
    {
        _trayLook.SetTrayIcon(look.Icon);
        _trayLook.SetTrayIconColour(look.Colour);
    }

    private void MakeTrayIconCards(ITrayIconLook look)
    {
        _trayLook = look;
        var colours = TrayIconChoices.Colours;
        int ColourIndex() => Math.Max(0, colours.ToList().FindIndex(c => c.Colour == look.TrayIconColour));
        _trayIcon = new MenuChoiceItem(TrayIconCard, () => TrayIconChoices.NameOf(look.TrayIcon), () => TrayIconChoices.IconRows(look, () => ChooseTrayIcon(look)))
        {
            Half = true,
        };
        _trayColour = new GalleryItem(TrayColourCard, ColourIndex(),
            i => (TrayIconChoices.Fill(colours[i].Colour), SwatchMark.None),
            i => look.SetTrayIconColour(colours[i].Colour))
        {
            Half = true,
        };
        Follow(h => look.TrayIconChanged += h, h => look.TrayIconChanged -= h, () =>
        {
            _trayIcon.Refresh();
            _trayColour.Load(ColourIndex());
        });
        Follow(h => AccentService.Changed += h, h => AccentService.Changed -= h, _trayColour.Refresh);
        _trayGrace = TimingCard(TrayGraceCard, look.ReopenGraceMs, TraySettings.ReopenGraceMin, TraySettings.ReopenGraceMax, " ms", TraySettings.ReopenGraceDefault, look.SetReopenGraceMs);
        _trayStagger = TimingCard(TrayStaggerCard, look.RecheckStaggerMs, TraySettings.RecheckStaggerMin, TraySettings.RecheckStaggerMax, " ms", TraySettings.RecheckStaggerDefault,
            look.SetRecheckStaggerMs);
        _trayGap = TimingCard(TrayGapCard, look.RecheckGapSeconds, TraySettings.RecheckGapMin, TraySettings.RecheckGapMax, " s", TraySettings.RecheckGapDefault, look.SetRecheckGapSeconds);
    }

    private static SliderItem TimingCard(SearchCard card, int value, int min, int max, string unit, int standard, Action<int> set) =>
        new(card, value, v =>
        {
            set(v);
            return OperationResult.Ok();
        }, _ => { }, min, max, unit, standard) { Half = true };

    // The menu closes first (its row was clicked), then THE icon picker opens over the main window.
    private static void ChooseTrayIcon(ITrayIconLook look) => Dispatcher.UIThread.Post(() => _ = Guard.RunAsync("Tray icon picker", async () =>
    {
        if (OwnedWindow.MainWindow is { } window) await look.ChooseTrayIconAsync(window);
    }));
}
