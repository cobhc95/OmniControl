using Avalonia.Data;

namespace OmniControl.App.Shared.Ui.Controls;

/// The master path field: every setting that takes a file, folder or program path uses it. The box
/// accepts typing and pasting and takes the text when it loses focus; "Browse…" opens the picker of its <see cref="Kind"/>
/// (<see cref="PathPicker"/>) and fills the box at once.
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

    /// Raised when the path was typed or browsed and differs from before; <see cref="Text"/> is the new path. A host that applies it (rather than binding two-way) listens here.
    public event EventHandler<RoutedEventArgs> Chosen { add => AddHandler(ChosenEvent, value); remove => RemoveHandler(ChosenEvent, value); }

    public static readonly StyledProperty<string> TypeNameProperty = AvaloniaProperty.Register<PathField, string>(nameof(TypeName), string.Empty);
    public static readonly StyledProperty<IReadOnlyList<string>?> PatternsProperty = AvaloniaProperty.Register<PathField, IReadOnlyList<string>?>(nameof(Patterns));

    /// The name of the file type the picker offers (with <see cref="Patterns"/>); empty: the kind's own.
    public string TypeName { get => GetValue(TypeNameProperty); set => SetValue(TypeNameProperty, value); }

    /// The file patterns the picker shows ("*.wav"); none: the kind's own.
    public IReadOnlyList<string>? Patterns { get => GetValue(PatternsProperty); set => SetValue(PatternsProperty, value); }

    private void Commit(string path)
    {
        if (path == Text) return;
        Text = path;
        RaiseEvent(new RoutedEventArgs(ChosenEvent, this));
    }

    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public PathKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    /// The grey example shown while the box is empty.
    public string Hint { get => GetValue(HintProperty); set => SetValue(HintProperty, value); }

    public string PickerTitle { get => GetValue(PickerTitleProperty); set => SetValue(PickerTitleProperty, value); }

    /// The text box, for the host's key handling (Enter leaves it) and focus.
    public TextBox Box => PathBox;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty) PathBox.Text = Text;
        else if (change.Property == HintProperty) PathBox.Watermark = Hint;
    }

    private void OnBrowseClick(object? sender, RoutedEventArgs e) => Guard.Fire("Browse for a path", async () =>
    {
        if (await PathPicker.PickAsync(this, Kind, PickerTitle, PathBox.Text, TypeName.Length > 0 ? TypeName : null, Patterns) is { } path) Commit(path);
    });
}
