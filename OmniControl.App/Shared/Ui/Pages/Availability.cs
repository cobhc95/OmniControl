namespace OmniControl.App.Shared.Ui.Pages;

/// Whether a card can be used right now and, when it cannot, why. A card that cannot is shown greyed out with the reason as its tooltip
/// (a monitor that does not report a control, a control still being looked up). Cards carry one; their rows read it.
public sealed partial class Availability : ObservableObject
{
    [ObservableProperty] private bool _isAvailable = true;

    /// The tooltip: why the card is unavailable; null while it is available (no tooltip).
    [ObservableProperty] private string? _reason;

    public void MakeAvailable() => (IsAvailable, Reason) = (true, null);

    public void MakeUnavailable(string reason) => (IsAvailable, Reason) = (false, reason);
}
