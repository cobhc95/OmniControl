using OmniControl.App.Features.Icons;

namespace OmniControl.App.Features.Tray;

/// The tray icon's look as the tray menu and the App settings cards read and set it (kept in tray.json).
public interface ITrayIconLook
{
    /// An icon value; null: the OmniControl logo.
    string? TrayIcon { get; }

    /// A colour of <see cref="TrayIconChoices.Colours"/>; null: Follow Windows.
    string? TrayIconColour { get; }

    void SetTrayIcon(string? icon);

    void SetTrayIconColour(string? colour);

    /// "Choose any icon…": THE icon picker over the window holding <paramref name="from"/>; its answer becomes the tray icon.
    Task ChooseTrayIconAsync(Visual from);

    event Action? TrayIconChanged;
}

/// The tray icon's choices, defined once for the tray menu and the App settings cards. Pure.
public static class TrayIconChoices
{
    public const string Accent = "accent", ChooseWords = "Choose any icon…", IconWords = "Tray icon", ColourWords = "Tray icon colour";

    public static readonly IReadOnlyList<(string? Icon, string Name)> Icons = new (string?, string)[]
    {
        (null, "OmniControl logo"), (IconLibrary.Logo, "Logo outline"), (IconLibrary.Mixer, "Sliders"), (IconLibrary.Command, "Command key"),
    };

    /// Follow Windows first, the accent, then drawing colours by their <see cref="Palette.DrawingNamed"/> names.
    public static readonly IReadOnlyList<(string? Colour, string Name)> Colours = new (string?, string)[]
    {
        (null, "Follow Windows"), (Accent, "Accent"), ("white", "White"), ("black", "Black"), ("green", "Green"), ("blue", "Blue"), ("purple", "Purple"), ("red", "Red"),
    };

    /// The rows of the icon menu that are always there (the choices' names, then "Choose any icon…"), for search.
    public static readonly IReadOnlyList<string> MenuWords = Icons.Select(c => c.Name).Append(ChooseWords).ToList();

    /// The chip's words for an icon: its choice's name, else the library icon's name.
    public static string NameOf(string? icon) =>
        Icons.FirstOrDefault(c => c.Icon == icon).Name ?? IconLibrary.Find(icon)?.Name ?? "Own icon";

    /// The colour an icon is drawn in; null keeps the logo exactly as it is. Follow Windows is white on a dark taskbar, black on a light one.
    public static Color? Ink(string? icon, string? colour, bool taskbarLight)
    {
        if (colour == Accent) return AccentService.Current;
        if (Palette.DrawingNamed.FirstOrDefault(d => d.Name == colour) is { Name: not null } named) return named.Colour;
        if (icon is null) return null;
        return taskbarLight ? Colors.Black : Colors.White;
    }

    /// A colour's dot: Follow Windows is half white, half black.
    public static IBrush Fill(string? colour) => colour is null
        ? new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.White, 0), new GradientStop(Colors.White, 0.5), new GradientStop(Colors.Black, 0.5), new GradientStop(Colors.Black, 1) },
        }
        : new SolidColorBrush(Ink(IconLibrary.Logo, colour, taskbarLight: false) ?? Colors.White);

    /// The App settings card's menu: the four icons (the one in use ticked), then "Choose any icon…".
    public static IReadOnlyList<MenuRowItem> IconRows(ITrayIconLook look, Action chooseAny) =>
        Icons.Select(c => new MenuRowItem(c.Name, string.Empty, look.TrayIcon == c.Icon, string.Empty, new RelayCommand(() => look.SetTrayIcon(c.Icon)), Icon: c.Icon))
            .Append(new MenuRowItem(ChooseWords, string.Empty, false, string.Empty, new RelayCommand(chooseAny), RuleAbove: true, IsMuted: true))
            .ToList();
}
