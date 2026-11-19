namespace OmniControl.App.Shared.Ui.Controls;

/// One preset of the menu; <see cref="IsCurrent"/> gets the tick.
public sealed record PresetRow(string Name, bool IsCurrent);

/// Wiring only: lists the presets it is given and reports a pick or "Manage presets…" to whoever opened it.
public partial class PresetMenuView : UserControl
{
    public PresetMenuView() => InitializeComponent();

    /// The muted title above the list ("Preset").
    public string Title { set => TitleText.Text = value; }

    /// The words of the row at the foot ("Manage presets…"); it raises <see cref="ManageRequested"/>.
    public string FooterText { set => FooterWords.Text = value; }

    /// A preset row was clicked (its name).
    public event EventHandler<string>? Picked;

    /// "Manage presets…" was clicked.
    public event EventHandler? ManageRequested;

    /// Shows <paramref name="names"/>, ticking <paramref name="current"/>.
    public void Show(IEnumerable<string> names, string current) =>
        List.ItemsSource = names.Select(n => new PresetRow(n, n == current)).ToList();

    private void OnRowClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is PresetRow row) Picked?.Invoke(this, row.Name);
    }

    private void OnManageClick(object? sender, RoutedEventArgs e) => ManageRequested?.Invoke(this, EventArgs.Empty);
}
