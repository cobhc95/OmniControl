using OmniControl.App.Features.OtherPcs;

namespace OmniControl.App.Features.OtherPcs.Pages;

/// One row of a checklist as the page draws it (Tailscale's, or "Can't reach it on this network?"): a mark, its words, a right-side tag and the one fix.
public sealed class CheckItem
{
    private readonly CheckRow _check;

    public CheckItem(CheckRow check, Action<string> runFix)
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

    /// The fixes that ask Windows' administrator prompt are pills; the others are links.
    public bool FixIsPill => _check.Fix.Length > 0 && _check.PillFix;

    public bool FixIsLink => _check.Fix.Length > 0 && !FixIsPill;

    public RelayCommand FixCommand { get; }
}
