using OmniControl.App.Features.Resources;

namespace OmniControl.App.Features.Traffic.Pages;

/// One direction's limit chip (Upload limit, Download limit) of a program's row and of the All apps row: its words and its menu, "No limit",
/// each offered limit, then Block (this direction alone), the one in force marked; a limit or No limit lifts the block.
internal sealed class LimitMenu
{
    /// A rule's value in one direction: a limit in KB/s, <see cref="None"/> or <see cref="Blocked"/>.
    public const int None = 0, Blocked = -1;

    public static readonly LimitMenu Upload = new(r => r.UploadBlocked, (r, on) => r.UploadBlocked = on, NetworkLimits.UploadBlockTip,
        r => r.UploadOn ? r.UploadKbps : None, (r, kbps) => (r.UploadOn, r.UploadKbps) = (kbps > 0, kbps > 0 ? kbps : r.UploadKbps));

    public static readonly LimitMenu Download = new(r => r.DownloadBlocked, (r, on) => r.DownloadBlocked = on, NetworkLimits.DownloadBlockTip,
        r => r.DownloadOn ? r.DownloadKbps : None, (r, kbps) => (r.DownloadOn, r.DownloadKbps) = (kbps > 0, kbps > 0 ? kbps : r.DownloadKbps));

    private readonly Func<AppNetRule, bool> _blocked;
    private readonly Action<AppNetRule, bool> _block;
    private readonly string _blockTip;
    private readonly Func<AppNetRule, int> _limit;
    private readonly Action<AppNetRule, int> _set;

    private LimitMenu(Func<AppNetRule, bool> blocked, Action<AppNetRule, bool> block, string blockTip, Func<AppNetRule, int> limit, Action<AppNetRule, int> set) =>
        (_blocked, _block, _blockTip, _limit, _set) = (blocked, block, blockTip, limit, set);

    public int Now(AppNetRule? rule) => rule is null ? None : _blocked(rule) ? Blocked : _limit(rule);

    public static string Words(int value) => value == Blocked ? AppNetItem.BlockWord : value == None ? AppNetItem.NoLimit : AppNetItem.Words(value);

    /// The menu's rows; <paramref name="current"/> null: the rows it stands for differ, so none is marked. <paramref name="change"/> applies a pick.
    public IReadOnlyList<MenuRowItem> Rows(int? current, Action<Action<AppNetRule>> change)
    {
        var rows = NetworkLimits.Choices.Prepend(None).Select(kbps => new MenuRowItem(Words(kbps), string.Empty, kbps == current, string.Empty,
            new RelayCommand(() => change(r => { _block(r, false); _set(r, kbps); })))).ToList();
        rows.Add(new MenuRowItem(AppNetItem.BlockWord, string.Empty, current == Blocked, _blockTip, new RelayCommand(() => change(r => _block(r, true))), RuleAbove: true, IsDanger: true));
        return rows;
    }
}
