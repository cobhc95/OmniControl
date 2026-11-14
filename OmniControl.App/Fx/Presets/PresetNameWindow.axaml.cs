using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace OmniControl.App.Fx.Presets;

/// <summary>Wiring only: asks for the name of a preset and closes with it (Save, Enter) or with null (Cancel, Esc). The rules come from the caller.</summary>
public partial class PresetNameWindow : Window
{
    private const string DefaultName = "My preset";
    private const string ReplaceNote = "A preset of this name exists: saving replaces it.";

    private readonly Func<string, string?> _error;
    private readonly Func<string, bool> _exists;

    public PresetNameWindow() : this(string.Empty, _ => null, _ => false)
    {
    }

    /// <param name="effect">The effect whose settings are kept.</param>
    /// <param name="error">Why a name cannot be used (null when it can).</param>
    /// <param name="exists">A preset of the user's own already has this name.</param>
    public PresetNameWindow(string effect, Func<string, string?> error, Func<string, bool> exists)
    {
        InitializeComponent();
        _error = error;
        _exists = exists;
        Prompt.Text = $"Name for the current settings of {effect}:";
        NameBox.Text = DefaultName;
        NameBox.TextChanged += (_, _) => Update();
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        Opened += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
        Update();
    }

    private string Typed => NameBox.Text?.Trim() ?? string.Empty;

    private void Update()
    {
        var problem = Typed.Length == 0 ? null : _error(Typed);
        SaveButton.IsEnabled = Typed.Length > 0 && problem is null;
        var note = problem ?? (Typed.Length > 0 && _exists(Typed) ? ReplaceNote : string.Empty);
        Note.Text = note;
        Note.IsVisible = note.Length > 0;
        Note.Classes.Set("caution", problem is not null);
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close(null);
        else if (e.Key == Key.Enter && SaveButton.IsEnabled) Close(Typed);
        else return;
        e.Handled = true;
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Close(Typed);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
