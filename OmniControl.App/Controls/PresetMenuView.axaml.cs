using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OmniControl.App.Controls;

/// <summary>One preset of the menu; <see cref="IsCurrent"/> gets the tick.</summary>
public sealed record PresetRow(string Name, bool IsCurrent);

/// <summary>Wiring only: lists the presets it is given and reports a pick or "Manage presets…" to whoever opened it.</summary>
public partial class PresetMenuView : UserControl
{
    public PresetMenuView() => InitializeComponent();

    /// <summary>The muted title above the list ("Preset").</summary>
    public string Title { set => TitleText.Text = value; }

    /// <summary>The words of the row at the foot ("Manage presets…"); it raises <see cref="ManageRequested"/>.</summary>
    public string FooterText { set => FooterWords.Text = value; }

    /// <summary>A preset row was clicked (its name).</summary>
    public event EventHandler<string>? Picked;

    /// <summary>"Manage presets…" was clicked.</summary>
    public event EventHandler? ManageRequested;

    /// <summary>Shows <paramref name="names"/>, ticking <paramref name="current"/>.</summary>
    public void Show(IEnumerable<string> names, string current) =>
        List.ItemsSource = names.Select(n => new PresetRow(n, n == current)).ToList();

    private void OnRowClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is PresetRow row) Picked?.Invoke(this, row.Name);
    }

    private void OnManageClick(object? sender, RoutedEventArgs e) => ManageRequested?.Invoke(this, EventArgs.Empty);
}
