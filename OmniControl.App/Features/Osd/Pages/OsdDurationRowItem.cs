using System.Globalization;
using OmniControl.App.Features.Osd;

namespace OmniControl.App.Features.Osd.Pages;

/// One exception row: its app or event (the chip and its menu), its seconds (typeable) and its remove cross.
public sealed partial class OsdDurationRowItem : ObservableObject
{
    private readonly OsdDurations _durations;
    private readonly Func<OsdDurationRule, IReadOnlyList<MenuRowItem>> _menu;
    private bool _loading;

    public OsdDurationRule Rule { get; }

    [ObservableProperty] private string _text = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SecondsText))]
    private double _seconds;

    public string SecondsText => Words(Seconds);

    public OsdDurationRowItem(OsdDurationRule rule, OsdDurations durations, Func<OsdDurationRule, IReadOnlyList<MenuRowItem>> menu)
    {
        (Rule, _durations, _menu) = (rule, durations, menu);
        Load();
    }

    /// The chip's menu as it is now.
    public IReadOnlyList<MenuRowItem> MenuRows => _menu(Rule);

    /// Shows the rule's values again without writing them back.
    public void Load()
    {
        _loading = true;
        Text = Rule.Describe();
        Seconds = Rule.Seconds;
        _loading = false;
    }

    partial void OnSecondsChanged(double value)
    {
        if (!_loading) _durations.SetSeconds(Rule, value);
    }

    [RelayCommand]
    private void Remove() => _durations.Remove(Rule);

    /// A time as the window shows it: "2.0 s".
    internal static string Words(double seconds) => seconds.ToString("F" + OsdDuration.Decimals, CultureInfo.CurrentCulture) + " s";
}
