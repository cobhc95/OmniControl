namespace OmniControl.App.Features.Resources.Gadget;

/// The Taskbar gadget tab's Font card beside Position: "Default (Segoe UI)" then every installed font, each row drawn in its own typeface. The
/// installed fonts are read off the UI thread when the menu opens and let go when it closes, so their rows exist only while it shows.
public sealed class GadgetFontCard
{
    public const string Label = "Font";

    private readonly Func<string> _font;
    private readonly Action<string> _use;
    private IReadOnlyList<string>? _installed;
    private bool _open;

    /// <param name="font">The font in force ("" = the default).</param>
    /// <param name="use">Uses a font (<see cref="IGadgetPreview.SetFont"/>).</param>
    public GadgetFontCard(Func<string> font, Action<string> use)
    {
        (_font, _use) = (font, use);
        Item = new MenuChoiceItem(Label, () => GadgetSettings.FontName(_font()), MenuRows)
        {
            Half = true, Tip = "The gadget's font, for every layout; the gadget still sizes to fit the taskbar", OpenChanged = OnOpenChanged,
        };
    }

    public MenuChoiceItem Item { get; }

    /// One row per font ("" = the default), each in its own typeface, <paramref name="current"/> chosen; a click uses it.
    public static IEnumerable<MenuRowItem> Rows(IEnumerable<string> fonts, string current, Action<string> use) =>
        fonts.Select(f => new MenuRowItem(GadgetSettings.FontName(f), string.Empty, string.Equals(f, current, StringComparison.OrdinalIgnoreCase), string.Empty,
            new RelayCommand(() => use(f)), Font: f.Length > 0 ? f : null));

    private IReadOnlyList<MenuRowItem> MenuRows() => Rows((_installed ?? Array.Empty<string>()).Prepend(string.Empty), _font(), _use).ToList();

    private void OnOpenChanged(bool open)
    {
        _open = open;
        if (!open)
        {
            _installed = null;
            return;
        }
        Guard.OffUi("Taskbar gadget: installed fonts", () =>
        {
            var names = FontManager.Current.SystemFonts.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
            Dispatcher.UIThread.Post(() =>
            {
                if (!_open) return;
                _installed = names;
                Item.Refresh();
            });
        });
    }
}
