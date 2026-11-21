namespace OmniControl.App.Features.Automation.Pages;

/// Wiring only: Ctrl+Tab switches between the tabs, and the Scripts tab's Import and Export open the file pickers. Everything else is in <see cref="AutomationPageViewModel"/> and the rows.
public partial class AutomationPage : UserControl
{
    public AutomationPage()
    {
        InitializeComponent();
        PageTabsView.Attach(this);
    }

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
