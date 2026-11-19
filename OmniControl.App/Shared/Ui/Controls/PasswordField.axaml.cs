using System.Windows.Input;
using Avalonia.Data;
using Avalonia.Input;

namespace OmniControl.App.Shared.Ui.Controls;

/// Wiring only: the typed text is handed on as it changes but nothing is stored until the Save pill or Enter runs <see cref="SaveCommand"/>
/// (the owner writes it and clears <see cref="Text"/>); <see cref="Dirty"/> is true while typed text waits, <see cref="SavedAt"/> and
/// <see cref="HasSaved"/> say what is stored. The eye shows the password only while it is held (mouse button, or Space or Enter while it
/// has focus); release, leaving it and Esc hide it.
public partial class PasswordField : UserControl
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<PasswordField, string>(nameof(Text), string.Empty, defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<string?> WatermarkProperty = AvaloniaProperty.Register<PasswordField, string?>(nameof(Watermark));
    public static readonly StyledProperty<ICommand?> SaveCommandProperty = AvaloniaProperty.Register<PasswordField, ICommand?>(nameof(SaveCommand));
    public static readonly StyledProperty<DateTime?> SavedAtProperty = AvaloniaProperty.Register<PasswordField, DateTime?>(nameof(SavedAt));
    public static readonly StyledProperty<bool> HasSavedProperty = AvaloniaProperty.Register<PasswordField, bool>(nameof(HasSaved));
    public static readonly StyledProperty<bool> DirtyProperty = AvaloniaProperty.Register<PasswordField, bool>(nameof(Dirty));

    public PasswordField()
    {
        InitializeComponent();
        Box.TextChanged += (_, _) => Text = Box.Text ?? string.Empty;
        Box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Save(); e.Handled = true; } };
        Eye.AddHandler(PointerPressedEvent, (_, _) => Show(true), RoutingStrategies.Tunnel, handledEventsToo: true);
        Eye.AddHandler(PointerReleasedEvent, (_, _) => Show(false), RoutingStrategies.Tunnel, handledEventsToo: true);
        Eye.AddHandler(PointerCaptureLostEvent, (_, _) => Show(false), RoutingStrategies.Bubble, handledEventsToo: true);
        Eye.AddHandler(KeyDownEvent, OnEyeKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        Eye.AddHandler(KeyUpEvent, (_, e) => { if (e.Key is Key.Space or Key.Enter) Show(false); }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Eye.LostFocus += (_, _) => Show(false);
        ShowState();
    }

    /// The password; set from the field when it is left, and an empty value from the owner empties the field.
    public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public string? Watermark { get => GetValue(WatermarkProperty); set => SetValue(WatermarkProperty, value); }

    /// Stores the typed password (Save pill or Enter); the owner clears <see cref="Text"/> once it is stored.
    public ICommand? SaveCommand { get => GetValue(SaveCommandProperty); set => SetValue(SaveCommandProperty, value); }

    /// When the password was last stored in this run; null when unknown (the tag then says only "saved in Windows").
    public DateTime? SavedAt { get => GetValue(SavedAtProperty); set => SetValue(SavedAtProperty, value); }

    /// Something is stored.
    public bool HasSaved { get => GetValue(HasSavedProperty); set => SetValue(HasSavedProperty, value); }

    /// Typed text is waiting to be saved.
    public bool Dirty { get => GetValue(DirtyProperty); private set => SetValue(DirtyProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty)
        {
            if (Box.Text != Text) Box.Text = Text;
            Dirty = Text.Length > 0;
        }
        else if (change.Property == WatermarkProperty) Box.Watermark = Watermark;
        if (change.Property == TextProperty || change.Property == HasSavedProperty || change.Property == SavedAtProperty
            || change.Property == SaveCommandProperty || change.Property == IsKeyboardFocusWithinProperty) ShowState();
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Save();

    private void Save()
    {
        if (Dirty && SaveCommand is { } save && save.CanExecute(null)) save.Execute(null);
    }

    private void ShowState()
    {
        SaveButton.IsEnabled = Dirty && SaveCommand?.CanExecute(null) != false;
        SavedTag.IsVisible = HasSaved && !Dirty;
        SavedText.Text = SavedAt is { } at ? $"saved in Windows · {at:HH:mm}" : "saved in Windows";
        UnsavedTag.IsVisible = Dirty && !IsKeyboardFocusWithin;
    }

    private void OnEyeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Space or Key.Enter) { Show(true); e.Handled = true; }
        else if (e.Key == Key.Escape) Show(false);
    }

    private void Show(bool show) => Box.RevealPassword = show;
}
