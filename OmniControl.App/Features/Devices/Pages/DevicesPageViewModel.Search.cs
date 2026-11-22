namespace OmniControl.App.Features.Devices.Pages;

/// The titles of a device's set-up stages, said once for the stages and for search.
internal static class DeviceStages
{
    public const string Python = "Python", Kasa = "python-kasa", Address = "Fixed address", SignIn = "Sign-in", Test = "Test",
        Emulator = "Emulator and connection", AppReady = "App ready", Steps = "Steps", Teach = "Teach on and off";

    /// Every stage of both methods, for search.
    public static IReadOnlyList<string> All { get; } = new[] { Python, Kasa, Address, SignIn, Emulator, AppReady, Steps, Teach, Test };
}

// What the Smart Devices page offers to global search, defined once (see SearchBuilder): the page's parts, the methods and set-up stages from
// their master lists, and one result per device (its name and methods) that opens the device on the page.
public sealed partial class DevicesPageViewModel
{
    private const string DeviceWords = "devices smart plug bulb socket outlet switch power on off";

    private static readonly SearchCard AddCard = new("Add device", DeviceWords + " new add", DeviceService.Category),
        ImportCard = new("Import from another PC", DeviceWords + " import copy paired other pc set-ups", DeviceService.Category),
        MethodsCard = new("Methods", DeviceWords + " method tapo kasa python-kasa scripts simulation emulator android adb", DeviceService.Category)
        {
            Choices = DeviceMethods.All.Select(m => m.Name).ToList(),
            Menu = DeviceMethods.All.SelectMany(m => new[] { m.Brief, m.Speed, m.Where }.Concat(m.Tags.Select(t => t.Text))).Distinct().ToList(),
            Tip = string.Join(" ", DeviceMethods.All.Select(m => m.Tip)),
        },
        StagesCard = new("Set-up stages", DeviceWords + " set up setup python kasa address sign-in password test teach steps emulator", DeviceService.Category) { Choices = DeviceStages.All },
        KeepRunningCard = new("Keep the emulator running", DeviceWords + " simulation emulator keep running always idle after each action memory minutes", DeviceService.Category),
        ListCard = new("Devices list", DeviceWords + " copy cut paste duplicate merge delete icon rename", DeviceService.Category)
        {
            Menu = new[] { CopyText, CutText, PasteText, DuplicateText, MergeText, DeleteText },
        };

    private static SearchCard DeviceCard(DeviceRecord record) =>
        new(record.Name, DeviceWords, DeviceService.Category) { Note = string.Join(", ", record.Methods.Select(record.NameOf)) };

    protected override SearchBuilder SearchCards => Cards(_service, this);

    /// THE definition of the page's search: its parts, the methods and set-up stages, and one result per device. A device's result is a fact
    /// card (its methods, an Open pill) made here; its pill selects the device and shows the page.
    private static SearchBuilder Cards(DeviceService? service, DevicesPageViewModel? page)
    {
        var builder = new SearchBuilder().Add(AddCard).Add(ImportCard).Add(MethodsCard).Add(StagesCard).Add(KeepRunningCard).Add(ListCard);
        if (service is null) return builder;
        return builder.AddEach(service.Records, DeviceCard, record => page?.Items.FirstOrDefault(i => i.Record == record) is { } item ? page.ResultFor(item, DeviceCard(record)) : null);
    }

    private FactItem ResultFor(DeviceItem item, SearchCard card) => new(card, () =>
    {
        Selected = item;
        _openPage();
    }) { ActionText = "Open" };

    /// The page's search words (<c>FeaturePage.Words</c>), read without making the page.
    public static IEnumerable<string> Words(DeviceService? service) => Cards(service, null).Words;
}
