using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Metadata;

namespace OmniControl.App.Picker;

/// <summary>
/// Picks the settings row's template by its kind (a number, choice or switch on the label line; a text or path on a line of its own) when the row is built,
/// so a row that the list reuses for another setting is rebuilt whole and never shows the previous setting's controls.
/// </summary>
public sealed class OptionTemplateSelector : IDataTemplate
{
    [Content] public IDataTemplate? Inline { get; set; }
    public IDataTemplate? Block { get; set; }

    public bool Match(object? data) => data is OptionItem;

    public Control? Build(object? param) =>
        (param is OptionItem { IsInline: true } ? Inline : Block)?.Build(param);
}
