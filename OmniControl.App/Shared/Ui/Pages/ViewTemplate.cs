using Avalonia.Controls.Templates;

namespace OmniControl.App.Shared.Ui.Pages;

/// The one way a feature maps its view-model to its view (as a DataTemplate with that DataType would): data of <typeparamref name="TModel"/> is shown as a new <typeparamref name="TView"/>.
public static class ViewTemplate
{
    // Matched by type, never by null: FuncDataTemplate<T> accepts null, so an empty ContentControl with a typed ContentTemplate fell through to the first app template.
    public static IDataTemplate For<TModel, TView>() where TView : Control, new() => new FuncDataTemplate(typeof(TModel), (_, _) => new TView());
}
