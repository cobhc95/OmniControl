using OmniControl.App.Features.OtherPcs;

namespace OmniControl.App.Features.OtherPcs.Pages;

/// One row of the "Over the internet" checklist as the page draws it: a mark, its words, a right-side tag and the one fix.
public sealed class TailscaleCheckItem
{
    private readonly TailscaleCheck _check;

    public TailscaleCheckItem(TailscaleCheck check, Action<string> runFix)
    {
        _check = check;
        FixCommand = new RelayCommand(() => runFix(check.Id));
    }

    public string Words => _check.Words;

    public bool IsOk => _check.State == CheckState.Ok;

    public bool IsFailed => _check.State == CheckState.Failed;

    public bool IsPending => _check.State == CheckState.Pending;

    /// The detail as a tooltip: why it fails (one sentence), or the account of a signed-in Tailscale.
    public string Tip => IsFailed ? _check.Sentence : _check.TagTip;

    public IReadOnlyList<TagItem> Tags => _check.Tag.Length > 0 ? new[] { new TagItem(_check.Tag) } : Array.Empty<TagItem>();

    public string FixText => _check.Fix;

    /// The firewall fix is a pill (it asks Windows' administrator prompt); the others are links.
    public bool FixIsPill => _check.Fix.Length > 0 && _check.Id == TailscaleChecks.Firewall;

    public bool FixIsLink => _check.Fix.Length > 0 && !FixIsPill;

    public RelayCommand FixCommand { get; }
}
