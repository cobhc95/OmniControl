using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Dialogs;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Automation;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Automation;

/// <summary>
/// One custom action on the Custom actions tab: its name, its type, its target and (for a script or program) its arguments. It is one
/// entry of the master list, so it appears in Shortcuts, the mouse and gesture pickers, search and every rule's action list. Changes apply at once.
/// </summary>
public sealed partial class CustomActionItem : ObservableObject, IEditorItem, IDisposable
{
    private readonly AutomationService _service;
    private readonly Func<DialogMessage, Task<bool>> _ask;

    public CustomAction Custom { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _kindName;
    [ObservableProperty] private string _target;
    [ObservableProperty] private string _arguments;

    public IReadOnlyList<string> KindNames => CustomKinds.Names;

    public string Label => Custom.Name;

    /// <summary>The type decides whether an arguments box is offered and what the target box suggests.</summary>
    public bool ShowsArguments => CustomKinds.TakesArguments(Custom.Kind);

    /// <summary>A file, script or program target is a path field (typed or browsed); a command or an address is plain words.</summary>
    public bool TakesPath => CustomKinds.PickerOf(Custom.Kind) is not null;

    public PathKind TargetPathKind => CustomKinds.PickerOf(Custom.Kind) ?? PathKind.File;

    public string TargetHint => CustomKinds.HintOf(Custom.Kind);

    public CustomActionItem(CustomAction action, AutomationService service, Func<DialogMessage, Task<bool>> ask)
    {
        Custom = action;
        _service = service;
        _ask = ask;
        _name = action.Name;
        _kindName = CustomKinds.NameOf(action.Kind);
        _target = action.Target;
        _arguments = action.Arguments;
        service.CustomChanged += OnChangedElsewhere;
    }

    public void Dispose() => _service.CustomChanged -= OnChangedElsewhere;

    // The Choose-the-action window edits the same action: this tab shows what it stored (the setters ignore a value that is already shown).
    private void OnChangedElsewhere(CustomAction custom)
    {
        if (custom != Custom) return;
        Name = custom.Name;
        KindName = CustomKinds.NameOf(custom.Kind);
        Target = custom.Target;
        Arguments = custom.Arguments;
    }

    partial void OnNameChanged(string value)
    {
        if (_service.EditCustom(Custom, CustomFields.Name, value) is not null) OnPropertyChanged(nameof(Label));
    }

    partial void OnKindNameChanged(string value)
    {
        _service.EditCustom(Custom, CustomFields.Kind, value);
        OnPropertyChanged(nameof(ShowsArguments));
        OnPropertyChanged(nameof(TargetHint));
        OnPropertyChanged(nameof(TakesPath));
        OnPropertyChanged(nameof(TargetPathKind));
    }

    partial void OnTargetChanged(string value) => _service.EditCustom(Custom, CustomFields.TargetText, value);

    partial void OnArgumentsChanged(string value) => _service.EditCustom(Custom, CustomFields.Arguments, value);

    public Task RunAsync() => _service.RunCustomAsync(Custom);

    /// <summary>Asks first; the action is removed when confirmed (a rule that used it then names an action that is no longer available).</summary>
    public async Task DeleteAsync()
    {
        if (!await _ask(Confirm.Delete("custom action", Custom.Name))) return;
        _service.RemoveCustom(Custom);
    }
}
