using Avalonia.Input;

namespace OmniControl.App.Shared.Ui.Pages;

/// THE look of every search box: a <c>TextBox</c> with the class <c>search</c> gets a muted magnifier at its left and the master remove badge
/// inside its right edge, shown only while the box has text. A click clears the box and keeps focus in it; Esc clears it too (and is used up
/// only then, so an empty box still lets Esc reach its window). A box that has a key to focus it names that key in <see cref="ShortcutProperty"/>
/// and shows it as a quiet pill while it is empty and not focused. <see cref="Register"/> is called once at start-up; no search box adds its own handler.
internal static class SearchClear
{
    private const string BoxClass = "search", BadgeClass = "removebadge", GlyphClass = "glyph", CrossKey = "CrossIcon",
        IconClass = "searchicon", IconKey = "Icon.search", PillClass = "shortcutpill";

    /// The keys that focus this search box, as words ("Ctrl F"); empty or null: no pill.
    public static readonly AttachedProperty<string?> ShortcutProperty = AvaloniaProperty.RegisterAttached<TextBox, string?>("Shortcut", typeof(SearchClear));

    public static string? GetShortcut(TextBox box) => box.GetValue(ShortcutProperty);

    public static void SetShortcut(TextBox box, string? value) => box.SetValue(ShortcutProperty, value);

    public static void Register()
    {
        Control.LoadedEvent.AddClassHandler<TextBox>(OnLoaded);
        InputElement.KeyDownEvent.AddClassHandler<TextBox>(OnKeyDown);
    }

    private static void OnLoaded(TextBox box, RoutedEventArgs e)
    {
        if (!box.Classes.Contains(BoxClass) || box.InnerRightContent is not null) return;
        var cross = new Avalonia.Controls.Shapes.Path { Classes = { GlyphClass } };
        if (box.TryFindResource(CrossKey, box.ActualThemeVariant, out var geometry) && geometry is Avalonia.Media.Geometry g) cross.Data = g;
        var badge = new Button { Classes = { BadgeClass }, Content = cross, Focusable = false, IsVisible = !string.IsNullOrEmpty(box.Text) };
        badge.Click += (_, _) => { Clear(box); };
        var word = new TextBlock();
        var pill = new Border { Classes = { PillClass }, Child = word, IsHitTestVisible = false };
        void Refresh()
        {
            var has = !string.IsNullOrEmpty(box.Text);
            badge.IsVisible = has;
            word.Text = GetShortcut(box);
            pill.IsVisible = !has && !box.IsFocused && !string.IsNullOrEmpty(word.Text);
        }
        box.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty || args.Property == InputElement.IsFocusedProperty || args.Property == ShortcutProperty) Refresh();
        };
        var magnifier = new Avalonia.Controls.Shapes.Path { Classes = { GlyphClass, IconClass } };
        if (box.TryFindResource(IconKey, box.ActualThemeVariant, out var icon) && icon is Avalonia.Media.Geometry ig) magnifier.Data = ig;
        box.InnerLeftContent = magnifier;
        box.InnerRightContent = new Panel { Children = { pill, badge } };
        Refresh();
    }

    private static void OnKeyDown(TextBox box, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || !box.Classes.Contains(BoxClass) || string.IsNullOrEmpty(box.Text)) return;
        Clear(box);
        e.Handled = true;
    }

    private static void Clear(TextBox box)
    {
        box.Text = string.Empty;
        box.Focus();
    }
}

