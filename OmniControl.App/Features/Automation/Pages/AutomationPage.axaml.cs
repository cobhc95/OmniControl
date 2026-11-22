using Avalonia.Input;
using Avalonia.VisualTree;

namespace OmniControl.App.Features.Automation.Pages;

/// Wiring only: Ctrl+Tab switches between the tabs, a rule asked for from elsewhere is scrolled to and focused, and the Scripts tab's Import and Export open the file pickers. Everything else is in <see cref="AutomationPageViewModel"/> and the rows.
public partial class AutomationPage : UserControl
{
    public AutomationPage()
    {
        InitializeComponent();
        PageTabsView.Attach(this);
    }

    private AutomationPageViewModel? _vm;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_vm is not null) _vm.RevealRequested -= Reveal;
        _vm = DataContext as AutomationPageViewModel;
        if (_vm is null) return;
        _vm.RevealRequested += Reveal;
        Reveal();
    }

    /// The row asked for (its tab is open by then) is scrolled to once laid out, and its first part takes the keyboard.
    private void Reveal() => Dispatcher.UIThread.Post(() => Guard.Run("Automation: show a rule", () =>
    {
        if (_vm?.TakeReveal() is not { } item) return;
        var list = this.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault(l => ReferenceEquals(l.ItemsSource, _vm.FeatureRules));
        if (list is null || ListScroll.Show(list, _vm.FeatureIndexOf(item)) is not { } row) return;
        row.BringIntoView();
        row.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(c => c.Focusable && c.IsEffectivelyVisible)?.Focus(NavigationMethod.Directional);
    }), DispatcherPriority.Loaded);

    private void OnImportScriptClick(object? sender, RoutedEventArgs e) => Guard.Fire("Automation: import a script", async () =>
    {
        if (DataContext is not AutomationPageViewModel vm) return;
        if (await PathPicker.PickAsync(this, PathKind.File, "Import a script", typeName: "Scripts", patterns: AutomationPageViewModel.ScriptPatterns) is { } path)
            await vm.ImportScriptAsync(path);
    });

    private void OnExportScriptClick(object? sender, RoutedEventArgs e) => Guard.Fire("Automation: export a script", async () =>
    {
        if (DataContext is not AutomationPageViewModel { ExportSuggestion: { } suggested } vm) return;
        if (await PathPicker.PickSaveAsync(this, "Export the script", suggested.Name, suggested.Extension, "Scripts", AutomationPageViewModel.ScriptPatterns) is { } path)
            await vm.ExportScriptAsync(path);
    });
}
