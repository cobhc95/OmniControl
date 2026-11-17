using CommunityToolkit.Mvvm.Input;
using OmniControl.App.OtherPcs;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.OtherPcs;

/// <summary>One row of the "Over the internet" checklist as the page draws it: a mark, its words, a right-side tag and the one fix.</summary>
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

    /// <summary>The detail as a tooltip: why it fails (one sentence), or the account of a signed-in Tailscale.</summary>
    public string Tip => IsFailed ? _check.Sentence : _check.TagTip;

    public IReadOnlyList<TagItem> Tags => _check.Tag.Length > 0 ? new[] { new TagItem(_check.Tag) } : Array.Empty<TagItem>();

    public string FixText => _check.Fix;

    /// <summary>The firewall fix is a pill (it asks Windows' administrator prompt); the others are links.</summary>
    public bool FixIsPill => _check.Fix.Length > 0 && _check.Id == TailscaleChecks.Firewall;

    public bool FixIsLink => _check.Fix.Length > 0 && !FixIsPill;

    public RelayCommand FixCommand { get; }
}
