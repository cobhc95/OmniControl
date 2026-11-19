using System.Windows.Input;

namespace OmniControl.App.Features.OtherPcs.Pages;

/// Layout and wiring of the Wake split button: the owner (a PC's header, this PC's title line) gives it the status words, what a click does and whether the
/// "Wake" action replaces the status; it decides nothing. The words and dot follow <see cref="Status"/>; while <see cref="IsWaking"/> they say "Waking…".
public partial class WakeSplit : UserControl
{
    public static readonly StyledProperty<TagItem?> StatusProperty = AvaloniaProperty.Register<WakeSplit, TagItem?>(nameof(Status));
    public static readonly StyledProperty<ICommand?> SetUpCommandProperty = AvaloniaProperty.Register<WakeSplit, ICommand?>(nameof(SetUpCommand));
    public static readonly StyledProperty<ICommand?> WakeCommandProperty = AvaloniaProperty.Register<WakeSplit, ICommand?>(nameof(WakeCommand));
    public static readonly StyledProperty<bool> ShowsWakeProperty = AvaloniaProperty.Register<WakeSplit, bool>(nameof(ShowsWake));
    public static readonly StyledProperty<bool> UnconfirmedProperty = AvaloniaProperty.Register<WakeSplit, bool>(nameof(Unconfirmed));
    public static readonly StyledProperty<bool> IsWakingProperty = AvaloniaProperty.Register<WakeSplit, bool>(nameof(IsWaking));
    public static readonly StyledProperty<string?> WakeTipProperty = AvaloniaProperty.Register<WakeSplit, string?>(nameof(WakeTip));

    public WakeSplit() => InitializeComponent();

    /// The wake status (words, tone, tooltip) shown in the left half while it is not the Wake action.
    public TagItem? Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    /// Opens the Set up wake window (the left status half and the cogwheel).
    public ICommand? SetUpCommand { get => GetValue(SetUpCommandProperty); set => SetValue(SetUpCommandProperty, value); }

    /// Sends the wake signal (the left half while it is the action).
    public ICommand? WakeCommand { get => GetValue(WakeCommandProperty); set => SetValue(WakeCommandProperty, value); }

    /// The left half is the "Wake" action instead of the status.
    public bool ShowsWake { get => GetValue(ShowsWakeProperty); set => SetValue(ShowsWakeProperty, value); }

    /// The Wake action carries a caution mark: the PC's wake could not be confirmed.
    public bool Unconfirmed { get => GetValue(UnconfirmedProperty); set => SetValue(UnconfirmedProperty, value); }

    /// The signal was sent and the PC is awaited.
    public bool IsWaking { get => GetValue(IsWakingProperty); set => SetValue(IsWakingProperty, value); }

    /// The Wake action's tooltip.
    public string? WakeTip { get => GetValue(WakeTipProperty); set => SetValue(WakeTipProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == StatusProperty || change.Property == IsWakingProperty)
        {
            Words.Text = IsWaking ? "Waking…" : Status?.Text;
            Dot.Tone = IsWaking ? null : Status?.Tone;
            Spinner.IsVisible = IsWaking;
            ToolTip.SetTip(StatusHalf, Status?.Tip);
        }
        else if (change.Property == ShowsWakeProperty)
        {
            ActionHalf.IsVisible = ShowsWake;
            StatusHalf.IsVisible = !ShowsWake;
            CogHalf.IsVisible = ShowsWake;
        }
        else if (change.Property == UnconfirmedProperty) Caution.IsVisible = Unconfirmed;
        else if (change.Property == WakeTipProperty) ToolTip.SetTip(ActionHalf, WakeTip);
    }

    private void OnSetUp(object? sender, RoutedEventArgs e) => Run(SetUpCommand);

    private void OnWake(object? sender, RoutedEventArgs e) => Run(WakeCommand);

    private static void Run(ICommand? command)
    {
        if (command?.CanExecute(null) == true) command.Execute(null);
    }
}
