using OmniControl.App.Features.Resources.Gadget;

namespace OmniControl.App.Features.Resources.Pages;

// What the Resources page offers to global search, defined once (see SearchBuilder): the Readings tab's cards from the reading list, the
// sensor and adapter menus, and the Taskbar gadget tab's cards (one list, GadgetSettingsModel.Cards), each with its live card when the page is made.
public sealed partial class ResourcesPageViewModel
{
    private const string ResourcesWords = "resources", ReadingWords = ResourcesWords + " readings";

    private const string LiveTip = "Readings come from Windows itself (GPU and disk load as Task Manager shows them, the graphics card's and drives' own temperatures); LibreHardwareMonitor for the CPU package temperature (it needs the PawnIO driver, else the ACPI thermal zone is read) and for what Windows does not give.";

    private static readonly SearchCard ReadEveryCard = new("Read every", ReadingWords + " interval seconds update poll", ReadingsTab)
        {
            Choices = Timing.ResourcesReadChoices.Select(Every).ToList(),
        },
        LiveGroupCard = new("Live readings", ReadingWords + " cpu gpu memory disk temperature network upload download", ReadingsTab) { Tip = LiveTip },
        TabsCard = new("Resources tabs", ResourcesWords, Title: PageTitle) { Choices = new[] { ReadingsTab, GadgetTab } };

    private static SearchCard ReadingSearch(Reading reading) =>
        new(reading.Card!.Value.Label, $"{ReadingWords} {reading.Name} {reading.Chip}", ReadingsTab);

    private static SearchCard SensorCard(Reading reading, string auto, string? tip) =>
        new(SensorMenu.Title, $"{ReadingWords} {reading.Name} temperature sensor hardware", ReadingsTab, $"{reading.Card!.Value.Label} sensor")
        {
            Menu = new[] { auto },
            Tip = tip,
        };

    private static SearchCard AdapterCard(Reading reading) =>
        new(NetworkAdapterMenu.ReadingTitle, $"{ReadingWords} {reading.Name} network adapter interface wifi ethernet vpn", ReadingsTab, $"{reading.Card!.Value.Label} adapter")
        {
            Menu = new[] { NetworkAdapterMenu.AutoBusiest, NetworkAdapterMenu.AllCombined },
        };

    protected override SearchBuilder SearchCards => Cards(_monitor, this);

    /// THE definition of the page's search: the Readings tab's cards (each reading's card, its sensor chip and, for speeds, its adapter chip),
    /// the poll interval, and the gadget tab's cards, each with its live card when <paramref name="page"/> is given.
    private static SearchBuilder Cards(IReadingList? monitor, ResourcesPageViewModel? page)
    {
        var builder = new SearchBuilder().Add(TabsCard).Add(LiveGroupCard).Add(ReadEveryCard, page?.ReadEvery);
        var readings = monitor?.Readings.Where(r => r.Card is not null).OrderBy(r => r.Card!.Value.Order).ToList() ?? new List<Reading>();
        LiveCard? Shown(Reading r) => page?._cards.GetValueOrDefault(r.Id);
        builder.AddEach(readings, ReadingSearch, r => Shown(r)?.Card);
        builder.AddEach(readings.Where(r => r.Id == Reading.HottestId), r => SensorCard(r, SensorMenu.HottestAuto, null), r => page?._hottest.Item);
        builder.AddEach(readings.Where(r => r.TemperatureId is not null && monitor?.Find(r.TemperatureId) is not null),
            r => SensorCard(r, SensorMenu.Auto, SensorMenu.TipFor(monitor!.Find(r.TemperatureId!)!)), r => Shown(r)?.Temperature?.Item);
        builder.AddEach(readings.Where(r => r.Kind == ReadingKind.Speed), AdapterCard, r => Shown(r)?.Card.Adapter);
        return builder.AddEach(GadgetSettingsModel.Cards, c => c, c => page?._gadget.SearchItems.FirstOrDefault(i => i.Label == c.Label));
    }

    /// The page's search words (<c>FeaturePage.Words</c>), read without making the page; only the gadget tab's cards while the poller does not run.
    public static IEnumerable<string> Words(IReadingList? monitor) => Cards(monitor, null).Words;
}
