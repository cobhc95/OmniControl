using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using OmniControl.App.Services;

namespace OmniControl.App.Controls;

/// <summary>
/// The master path field (AGENTS.md "Master design elements"): every setting that takes a file, folder or program path uses it. The box
/// accepts typing and pasting and takes the text when it loses focus; "Browse…" opens the picker of its <see cref="Kind"/>
/// (<see cref="PathPicker"/>) and fills the box at once.
/// </summary>
public partial class PathField : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<PathField, string>(nameof(Text), string.Empty, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<PathKind> KindProperty = AvaloniaProperty.Register<PathField, PathKind>(nameof(Kind), PathKind.File);
    public static readonly StyledProperty<string> HintProperty = AvaloniaProperty.Register<PathField, string>(nameof(Hint), string.Empty);
    public static readonly StyledProperty<string> PickerTitleProperty = AvaloniaProperty.Register<PathField, string>(nameof(PickerTitle), "Choose");

    public static readonly RoutedEvent<RoutedEventArgs> ChosenEvent = RoutedEvent.Register<PathField, RoutedEventArgs>(nameof(Chosen), RoutingStrategies.Bubble);

    public PathField()
    {
        InitializeComponent();
        PathBox.LostFocus += (_, _) => Commit(PathBox.Text ?? string.Empty);
    }

    /// <summary>Raised when the path was typed or browsed and differs from before; <see cref="Text"/> is the new path. A host that applies it (rather than binding two-way) listens here.</summary>
    public event EventHandler<RoutedEventArgs> Chosen { add => AddHandler(ChosenEvent, value); remove => RemoveHandler(ChosenEvent, value); }

    /// <summary>The name of the file type the picker offers (with <see cref="Patterns"/>); empty: the kind's own.</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>The file patterns the picker shows ("*.wav"); none: the kind's own.</summary>
    public IReadOnlyList<string>? Patterns { get; set; }

    private void Commit(string path)
    {
        if (path == Text) return;
        Text = path;
        RaiseEvent(new RoutedEventArgs(ChosenEvent, this));
    }

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public PathKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    /// <summary>The grey example shown while the box is empty.</summary>
    public string Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }

    /// <summary>The title of the picker window.</summary>
    public string PickerTitle { get => GetValue(PickerTitleProperty); set => SetValue(PickerTitleProperty, value); }

    /// <summary>The text box, for the host's key handling (Enter leaves it) and focus.</summary>
    public TextBox Box => PathBox;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) PathBox.Text = Text;
        else if (change.Property == HintProperty) PathBox.Watermark = Hint;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (await PathPicker.PickAsync(this, Kind, PickerTitle, PathBox.Text, TypeName.Length > 0 ? TypeName : null, Patterns) is { } path) Commit(path);
    }
}
