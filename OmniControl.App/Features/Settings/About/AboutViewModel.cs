namespace OmniControl.App.Features.Settings.About;

/// The About window: OmniControl's licence notice and one card per credited project, all from NOTICE.txt.
public sealed class AboutViewModel
{
    private readonly Action<string> _report;

    public AboutViewModel(Action<string> report)
    {
        _report = report;
        var notice = Credits.NoticeText();
        Notice = Credits.Preamble(notice);
        Entries = Credits.Entries(notice).Select(e => new CreditCard(e, OpenSite)).ToList();
    }

    public string Version => AppInfo.NameAndVersion;
    public string Notice { get; }
    public IReadOnlyList<CreditCard> Entries { get; }

    private void OpenSite(string url)
    {
        if (WebPage.Open(url) is { } error) _report(error);
    }
}

/// One credited project as the About window shows it.
public sealed class CreditCard
{
    public CreditCard(CreditEntry entry, Action<string> open)
    {
        Entry = entry;
        OpenCommand = new RelayCommand(() => open(entry.Website), () => entry.Website.Length > 0);
    }

    private CreditEntry Entry { get; }
    public string Name => Entry.Name;
    public string Version => Entry.Version;
    public string Licence => Entry.Licence;
    public string Copyright => Entry.Copyright;
    public string UsedFor => Entry.UsedFor;
    public string Website => Entry.Website;
    public IRelayCommand OpenCommand { get; }
}
