using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Pages;

/// <summary>
/// THE clear button of every search box: a <c>TextBox</c> with the class <c>search</c> gets the master remove badge inside its right edge,
/// shown only while the box has text. A click clears the box and keeps focus in it; Esc clears it too (and is used up only then, so an
/// empty box still lets Esc reach its window). <see cref="Register"/> is called once at start-up; no search box adds its own handler.
/// </summary>
internal static class SearchClear
{
    private const string BoxClass = "search", BadgeClass = "removebadge", GlyphClass = "glyph", CrossKey = "CrossIcon";

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
        box.PropertyChanged += (_, args) =>
        {
            if (args.Property == TextBox.TextProperty) badge.IsVisible = !string.IsNullOrEmpty(box.Text);
        };
        box.InnerRightContent = badge;
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
