using Avalonia.Platform;
using Avalonia.Styling;

namespace OmniControl.App.Shared.Services;

/// One background colour choice for one theme: the page (window), the card (groups, panes, side bars, pop-downs, the tray panel), a raised
/// fill (a hovered row or card), the edge (dividers, a panel's outline) and a control's own fill (switch, chip, slider knob, tile), then the
/// setting cards inside a group: their fill, edge and the fill of the selected one.
public readonly record struct SurfaceSet(Color Page, Color Card, Color Raised, Color Edge, Color Control, Color Item, Color ItemEdge, Color ItemSelected);

/// The one place the program's surface colours live (the background colour choice of App settings > Appearance). Every surface brush the
/// styles use (BrushBackground, BrushSidebar, BrushHover, BrushDivider, BrushControlFill and the brushes worked out from them below) is made
/// here for both themes from the chosen <see cref="SurfaceSet"/> and updates live; nothing else defines a surface colour. "Follow Windows"
/// takes Windows' own tones: the Windows 11 standard (Carbon), and Black while Windows asks for high contrast (read through Avalonia's platform
/// settings, which hear Windows' colour change notice). Windows does not report its surface colours any finer, and older Windows gets Carbon.
public static class SurfaceService
{
    public const string FollowName = "Follow Windows";
    private const string CarbonName = "Carbon", BlackName = "Black";

    private static readonly IReadOnlyList<(string Name, SurfaceSet Dark, SurfaceSet Light)> Sets = new[]
    {
        (CarbonName, Dark("#202020", "#1C1C1C", "#383838", "#2E2E2E", "#474747"), Light("#F3F3F3", "#FBFBFB", "#EAEAEA", "#E0E0E0", "#DCDCDC")),
        ("Slate blue", Dark("#1C2433", "#18202D", "#313C4D", "#2E3847", "#3E4A5C"), Light("#EEF1F6", "#F8FAFC", "#E3E8EF", "#D6DDE7", "#D3DAE4")),
        // Nothing is darker than a black page, so here the group is a faint lift and only the cards inside it step down to black
        (BlackName, Dark("#000000", "#0E0E0E", "#222222", "#2A2A2A", "#333333", "#070707", "#222222", "#000000"), Light("#FFFFFF", "#F7F7F7", "#EBEBEB", "#DEDEDE", "#DADADA")),
        ("Soft plum", Dark("#2B2A33", "#26252D", "#42404C", "#3A3944", "#504E5C"), Light("#F4F3F7", "#FBFAFD", "#E9E7EF", "#DDDAE4", "#D9D6E1")),
    };

    /// Every choice in the order the gallery shows it: Follow Windows, then the fixed sets.
    public static IReadOnlyList<string> Names { get; } = Sets.Select(s => s.Name).Prepend(FollowName).ToList();

    public static string Name { get; private set; } = FollowName;

    /// Raised after the brushes changed (a new choice, or Windows' contrast setting under Follow Windows).
    public static event Action? Changed;

    private static readonly ThemeBrushes Written = new();

    public static int IndexOf(string name) => Math.Max(0, Names.ToList().IndexOf(name));

    /// The page colour now in use for <paramref name="variant"/> (the accent's soft washes are blended into it).
    public static Color PageOf(ThemeVariant variant) => SetOf(Name, variant).Page;

    /// The colour a choice's gallery dot shows: its dark page, the tone that tells the choices apart best.
    public static Color Swatch(string name) => SetOf(name, ThemeVariant.Dark).Page;

    /// Applies the saved choice and starts listening to Windows' colour change notice (once, for the program's life).
    public static void Initialize(string name)
    {
        Apply(name);
        if (Application.Current?.PlatformSettings is { } platform) platform.ColorValuesChanged += OnWindowsColours;
        AccentService.Changed += FollowAccentWash;
        // The accent is applied after this, so the light theme's focus fill is read once start-up has finished
        Dispatcher.UIThread.Post(FollowAccentWash);
    }

    public static void Set(string name)
    {
        Apply(name);
        Changed?.Invoke();
        FollowAccentWash(); // the accent re-blends its washes into the new page inside Changed
    }

    /// A focused card's fill: the darkest card shade on the dark theme (Update), the accent's own wash on the light theme, copied here
    /// whenever the accent changes so the light theme looks as it always did.
    private static void FollowAccentWash()
    {
        if (Application.Current is not { } app) return;
        var wash = ThemeBrushes.DictionaryOf(app, ThemeVariant.Light).TryGetResource("BrushAccentSoft", ThemeVariant.Light, out var v) && v is ISolidColorBrush b
            ? b.Color : Colors.Transparent;
        Written.Set(app, ThemeVariant.Light, "BrushCardFocusFill", wash);
    }

    private static void OnWindowsColours(object? sender, PlatformColorValues e)
    {
        if (Name == FollowName) Dispatcher.UIThread.Post(() => Guard.Run("Windows colour change", () => Set(Name)));
    }

    private static SurfaceSet SetOf(string name, ThemeVariant variant)
    {
        var set = Sets.FirstOrDefault(s => s.Name == (name == FollowName ? WindowsChoice() : name));
        if (set.Name is null) set = Sets[0];
        return variant == ThemeVariant.Light ? set.Light : set.Dark;
    }

    private static string WindowsChoice() =>
        Application.Current?.PlatformSettings?.GetColorValues().ContrastPreference == ColorContrastPreference.High ? BlackName : CarbonName;

    private static void Apply(string name)
    {
        Name = Names.Contains(name) ? name : FollowName;
        if (Application.Current is not { } app) return;
        Update(app, ThemeVariant.Dark, Colors.White);
        Update(app, ThemeVariant.Light, Colors.Black);
    }

    /// Writes one theme's surface brushes; the control shades step from the control fill towards <paramref name="lift"/> (white on the
    /// dark theme, black on the light one), so a control always reads against every container fill.
    private static void Update(Application app, ThemeVariant variant, Color lift)
    {
        var s = SetOf(Name, variant);
        Color Lifted(double share) => ColorMath.Mix(s.Control, lift, share);
        void Put(string key, Color color) => Written.Set(app, variant, key, color);

        Put("BrushBackground", s.Page);
        Put("BrushSidebar", s.Card);
        Put("BrushHover", s.Raised);
        Put("BrushDivider", s.Edge);
        Put("BrushControlFill", s.Control);
        Put("BrushCard", s.Item);
        Put("BrushCardEdge", s.ItemEdge);
        Put("BrushCardSelected", s.ItemSelected);
        // The lead row of a list (a whole-list row above its items): a step lighter fill and a stronger edge than the cards, in both themes
        Put("BrushCardLead", variant == ThemeVariant.Dark ? ColorMath.Mix(s.Item, lift, LeadLift) : ColorMath.Mix(s.Card, s.Raised, LeadLightFill));
        Put("BrushCardLeadEdge", ColorMath.Mix(s.ItemEdge, lift, LeadEdge));
        // A group inside a group: dark steps half way from the group to its cards, so the cards inside still read; light keeps its raised panel
        Put("BrushSubgroup", variant == ThemeVariant.Dark ? ColorMath.Mix(s.Card, s.Item, 0.5) : s.Raised);
        if (variant == ThemeVariant.Dark) Put("BrushCardFocusFill", s.ItemSelected);
        // A band inside a panel (the tray panel's sliders): the dark theme's card fill, the light theme's panel a step towards the page
        Put("BrushCardShade", variant == ThemeVariant.Dark ? s.Item : ColorMath.Mix(s.Card, s.Page, 0.5));
        Put("BrushField", ColorMath.Mix(s.Card, s.Raised, 0.4));
        // A menu's chosen row: a faint fill between the menu's surface and its hover, so hover still shows on it
        Put("BrushMenuChosen", ColorMath.Mix(s.Card, s.Raised, 0.6));
        Put("BrushScrollTrack", ColorMath.Mix(s.Card, s.Edge, 0.7));
        Put("BrushControlTrack", Lifted(0.07));
        Put("BrushHoverStrong", Lifted(0.08));
        Put("BrushControlEdge", Lifted(0.18));
        Put("BrushFieldEdge", Lifted(0.18));
        Put("BrushGrip", Lifted(0.24));
        Put("BrushHoverEdge", Lifted(0.27));
        Put("BrushScrollThumb", Lifted(0.3));
        // Windows' slider over a card: its track is the strong control fill, its knob's edge the faint elevation edge
        var dark = variant == ThemeVariant.Dark;
        Put("BrushSliderTrack", ColorMath.Mix(s.Card, lift, dark ? SliderTrackDark : SliderTrackLight));
        Put("BrushSliderKnobEdge", ColorMath.Mix(s.Card, lift, dark ? KnobEdgeDark : KnobEdgeLight));

        // Windows' switch while off: a see-through track (a faint wash under the pointer), a clear outline and a light knob on the dark
        // theme, a dark one on the light theme. The on look is the accent's (AccentService).
        var outline = ColorMath.Mix(s.Page, lift, OffOutline);
        var knob = ColorMath.Mix(s.Page, lift, variant == ThemeVariant.Light ? OffOutline : OffKnobDark);
        var wash = Color.FromArgb(OffWashAlpha, lift.R, lift.G, lift.B);
        // Windows' SubtleFillSecondary / Tertiary: a see-through wash for buttons whose words are accent-coloured, so the words keep their contrast
        Put("BrushHoverSubtle", Color.FromArgb(dark ? SubtleHoverDark : SubtleHoverLight, lift.R, lift.G, lift.B));
        Put("BrushPressedSubtle", Color.FromArgb(dark ? SubtlePressedDark : SubtlePressedLight, lift.R, lift.G, lift.B));
        foreach (var state in new[] { "", "PointerOver", "Pressed" })
        {
            Put($"ToggleSwitchFillOff{state}", state.Length == 0 ? Colors.Transparent : wash);
            Put($"ToggleSwitchStrokeOff{state}", outline);
            Put($"ToggleSwitchKnobFillOff{state}", knob);
        }
    }

    /// The off switch's outline and the dark theme's off knob as shares of the way from the page to the text colour (Windows: 60 % and 79 %
    /// white on the dark theme), and its hover wash's strength.
    private const double OffOutline = 0.6, OffKnobDark = 0.79;
    private const byte OffWashAlpha = 0x14;

    /// The subtle hover and pressed washes' strength: white at 6 % and 4 % on the dark theme, black at 4 % and 2 % on the light theme.
    private const byte SubtleHoverDark = 0x0F, SubtlePressedDark = 0x0A, SubtleHoverLight = 0x0A, SubtlePressedLight = 0x05;

    /// Windows 11's ControlStrongFill (#8BFFFFFF dark, #72000000 light) and the knob's ControlElevationBorder (#18FFFFFF, #0F000000) as shares.
    private const double SliderTrackDark = 0.545, SliderTrackLight = 0.447, KnobEdgeDark = 0.094, KnobEdgeLight = 0.059;

    /// The light theme's cards have no fill of their own (the group shows through) and a selected one takes the group's fill.
    private static SurfaceSet Light(string page, string card, string raised, string edge, string control) =>
        new(Color.Parse(page), Color.Parse(card), Color.Parse(raised), Color.Parse(edge), Color.Parse(control), Colors.Transparent, Color.Parse(edge), Color.Parse(card));

    /// The dark theme steps down: page, then the group (<paramref name="card"/>), then the cards inside it, then the selected card darkest.
    /// Unless given, the card and the selected card are the group darkened by fixed shares and the card's edge sits a quarter of the way
    /// from the group's edge towards the group's fill, so every choice gets the same steps.
    private static SurfaceSet Dark(string page, string card, string raised, string edge, string control,
        string? item = null, string? itemEdge = null, string? selected = null)
    {
        var group = Color.Parse(card);
        var groupEdge = Color.Parse(edge);
        return new(Color.Parse(page), group, Color.Parse(raised), groupEdge, Color.Parse(control),
            item is null ? ColorMath.Mix(group, Colors.Black, ItemDarken) : Color.Parse(item),
            itemEdge is null ? ColorMath.Mix(groupEdge, group, ItemEdgeBlend) : Color.Parse(itemEdge),
            selected is null ? ColorMath.Mix(group, Colors.Black, SelectedDarken) : Color.Parse(selected));
    }

    private const double ItemDarken = 0.14, SelectedDarken = 0.36, ItemEdgeBlend = 0.25;

    /// The lead row: its dark fill a little towards white, its light fill most of the way to the raised tone, its edge towards the text colour.
    private const double LeadLift = 0.04, LeadLightFill = 0.6, LeadEdge = 0.14;
}
