namespace OmniControl.App.Shared.Ui.Controls;

/// THE colour editor's layout (see ColourEditor.axaml); the model is a <see cref="ColourEditorModel"/>. <see cref="Extra"/> = rows of the owner's
/// own under the sliders (the accent's Intensity), on the same data context.
public partial class ColourEditor : UserControl
{
    public static readonly StyledProperty<Control?> ExtraProperty = AvaloniaProperty.Register<ColourEditor, Control?>(nameof(Extra));

    public ColourEditor() => InitializeComponent();

    public Control? Extra { get => GetValue(ExtraProperty); set => SetValue(ExtraProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ExtraProperty) return;
        ExtraPresenter.Content = Extra;
        ExtraPresenter.IsVisible = Extra is not null;
    }
}
