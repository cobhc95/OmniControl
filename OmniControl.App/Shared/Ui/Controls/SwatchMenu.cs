using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;

namespace OmniControl.App.Shared.Ui.Controls;

/// THE colour pop-down's content: the grid of dots (<see cref="SwatchList"/>), then, when <see cref="EditorText"/> is set, a thin line and the muted
/// "Custom colour…" row (Down past the dots reaches it, Up goes back). The row closes the pop-down and raises <see cref="EditorClicked"/> with the
/// control the pop-down was opened at; the owner then opens its editor there (<see cref="OpenEditor"/>).
public sealed class SwatchMenu : StackPanel
{
    public static readonly StyledProperty<IEnumerable<SwatchItem>?> ItemsSourceProperty = AvaloniaProperty.Register<SwatchMenu, IEnumerable<SwatchItem>?>(nameof(ItemsSource));
    public static readonly StyledProperty<string?> EditorTextProperty = AvaloniaProperty.Register<SwatchMenu, string?>(nameof(EditorText));

    private readonly SwatchList _list = new() { IsMenu = true, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly Border _rule = new() { IsVisible = false };
    private readonly TextBlock _words = new();
    private readonly Button _row;

    public SwatchMenu()
    {
        _rule.Classes.Add("hrule");
        _row = new Button { Content = _words, IsVisible = false };
        _row.Classes.AddRange(new[] { "menurow", "muted" });
        _row.Click += (_, _) => OpenFromRow();
        _row.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Up || e.KeyModifiers != KeyModifiers.None) return;
            _list.FocusChosen();
            e.Handled = true;
        };
        _list.PastEnd += () => { if (_row.IsVisible) _row.Focus(NavigationMethod.Directional); };
        Children.AddRange(new Control[] { _list, _rule, _row });
        this.Bind(SpacingProperty, this.GetResourceObservable("Gap"));
    }

    public IEnumerable<SwatchItem>? ItemsSource { get => GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }

    /// The editor row's words ("Custom colour…"); null: no editor row.
    public string? EditorText { get => GetValue(EditorTextProperty); set => SetValue(EditorTextProperty, value); }

    /// The editor row was chosen; the argument is the control the pop-down was opened at.
    public event EventHandler<Control>? EditorClicked;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemsSourceProperty) _list.ItemsSource = ItemsSource;
        else if (change.Property == EditorTextProperty)
        {
            _words.Text = EditorText;
            _rule.IsVisible = _row.IsVisible = EditorText is not null;
        }
    }

    // The pop-down closes first; the editor then opens at the same opener.
    private void OpenFromRow()
    {
        var popup = this.FindLogicalAncestorOfType<Popup>();
        var opener = popup?.PlacementTarget;
        popup?.Close();
        if (opener is not null) Dispatcher.UIThread.Post(() => EditorClicked?.Invoke(this, opener));
    }

    /// THE colour editor's pop-down (the master card pop-down, <c>optionmenu</c>) under <paramref name="opener"/>, its content made fresh by
    /// <paramref name="make"/>, which is given the action that closes it.
    public static void OpenEditor(Control opener, Func<Action, Control> make)
    {
        var flyout = new ListMenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.FlyoutPresenterClasses.Add("optionmenu");
        flyout.Content = make(flyout.Hide);
        flyout.ShowAt(opener);
    }
}
