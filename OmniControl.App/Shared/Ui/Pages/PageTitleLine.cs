using Avalonia.Controls.Primitives;

namespace OmniControl.App.Shared.Ui.Pages;

/// THE title line of a page (look in Styles/Theme.axaml): the page's <see cref="Title"/>, a <see cref="Middle"/> slot beside it (a switch
/// card, Easy | Advanced) and a <see cref="Right"/> slot at the far right (Restore defaults, a preset picker, Pause). Every page that
/// shows its own header uses this one control, so the spacing is the same everywhere.
public class PageTitleLine : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty = AvaloniaProperty.Register<PageTitleLine, string?>(nameof(Title));
    public static readonly StyledProperty<object?> MiddleProperty = AvaloniaProperty.Register<PageTitleLine, object?>(nameof(Middle));
    public static readonly StyledProperty<object?> RightProperty = AvaloniaProperty.Register<PageTitleLine, object?>(nameof(Right));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public object? Middle { get => GetValue(MiddleProperty); set => SetValue(MiddleProperty, value); }

    public object? Right { get => GetValue(RightProperty); set => SetValue(RightProperty, value); }
}
