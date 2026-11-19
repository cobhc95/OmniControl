namespace OmniControl.App.Shared.Ui.Controls;

/// Shows a <see cref="RemotePcLabel"/> (the PC an entry runs on); it is hidden when there is none, so a list binds it for every row.
public partial class RemoteSectionTag : UserControl
{
    public static readonly StyledProperty<RemotePcLabel?> LabelProperty = AvaloniaProperty.Register<RemoteSectionTag, RemotePcLabel?>(nameof(Label));

    public RemoteSectionTag()
    {
        InitializeComponent();
        IsVisible = false;
    }

    public RemotePcLabel? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != LabelProperty) return;
        Body.DataContext = Label; // the inner panel only: the control's own DataContext feeds its Label binding
        IsVisible = Label is not null;
    }
}
