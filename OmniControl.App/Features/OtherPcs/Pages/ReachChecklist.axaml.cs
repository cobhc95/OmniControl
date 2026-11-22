namespace OmniControl.App.Features.OtherPcs.Pages;

/// Wiring only: the <see cref="ReachHelp"/> handed in is the data of the panel inside (the control's own DataContext stays its parent's, so its bindings keep working).
public partial class ReachChecklist : UserControl
{
    public static readonly StyledProperty<ReachHelp?> HelpProperty = AvaloniaProperty.Register<ReachChecklist, ReachHelp?>(nameof(Help));

    public ReachChecklist() => InitializeComponent();

    public ReachHelp? Help
    {
        get => GetValue(HelpProperty);
        set => SetValue(HelpProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == HelpProperty) Root.DataContext = Help;
    }
}
