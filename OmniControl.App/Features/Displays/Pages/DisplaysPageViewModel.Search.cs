using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Displays.Pages;

// What the Displays page offers to global search, defined once (see SearchBuilder): the group tips, each display's controls that it has, the
// whole-desktop cards and the two preset drop-downs (with their preset names), each with its live card when the page is made.
public sealed partial class DisplaysPageViewModel
{
    private const string HardwareTip = "Talks to each monitor over DDC/CI (it must be on in the monitor's own menu); controls a monitor does not report are greyed out. "
        + "Turn screen off sends it to sleep: wake it with its button or by using its input, as the monitor allows.";

    private const string SoftwareTip = "Applied by OmniControl on any display and put back when it closes (the saved presets and the warm-light schedule are kept). "
        + "Colour temperature: minus is warmer, plus is cooler. Saturation, grayscale and invert apply to all displays. "
        + "A display whose driver does not accept colour changes from programs is greyed out.";

    private const string SoftwareRestoreTip = "Puts every display's software picture, saturation, grayscale, invert and the warm-light schedule back to normal. Presets are kept.";

    private static readonly SearchCard HardwareGroupCard = new(DisplayControls.HardwareGroup, "monitor display screen ddc ci hardware turn screen off sleep wake", DisplayControls.HardwareGroup)
        {
            Tip = HardwareTip,
        },
        SoftwareGroupCard = new(DisplayControls.SoftwareGroup, "monitor display screen software digital picture restore defaults", DisplayControls.SoftwareGroup)
        {
            Tip = SoftwareTip,
            Note = SoftwareRestoreTip,
        };

    protected override SearchBuilder SearchCards => Cards(_service, this);

    /// THE definition of the page's search: the group tips, then for each display the controls it has (the ones it does not report are not
    /// offered), the whole-desktop cards and the preset drop-downs, each with its live card when <paramref name="page"/> is given.
    private static SearchBuilder Cards(DisplaysService? service, DisplaysPageViewModel? page)
    {
        var builder = new SearchBuilder().Add(HardwareGroupCard).Add(SoftwareGroupCard);
        if (service is null) return builder;
        var hardware = DisplayControls.Hardware.Select(l => DisplayCards.Level(l.Spec, DisplayControls.HardwareGroup))
            .Concat(DisplayControls.HardwareChoices.Select(DisplayCards.Choice)).Append(DisplayCards.Orientation).ToList();
        foreach (var display in service.HardwareDisplays)
        {
            var available = display.AvailableLabels.ToHashSet();
            var item = page?._hardware.GetValueOrDefault(display);
            builder.AddEach(hardware.Where(c => available.Contains(c.Label)), c => DisplayCards.Of(c, display.Name), c => item?.AvailableCards.FirstOrDefault(e => e.Label == c.Label));
        }
        builder.AddEach(DisplayCards.Desktop, c => c, c => page?.Software.Items.Concat(page._warm.Cards).OfType<IEditorItem>().FirstOrDefault(e => e.Label == c.Label));
        var software = DisplayControls.Software.Select(l => DisplayCards.Level(l.Spec, DisplayControls.SoftwareGroup)).ToList();
        foreach (var display in service.SoftwareDisplays)
        {
            var item = page?._software.GetValueOrDefault(display);
            builder.AddEach(software, c => DisplayCards.Of(c, display.Name), c => item?.Cards.FirstOrDefault(e => e.Label == c.Label));
        }
        return builder
            .Add(PresetsCard(service.PicturePresetsLabel, service.PicturePresets, DisplayControls.SoftwareGroup, "Display presets", "display picture"), page?.PresetChoice)
            .Add(PresetsCard(service.MonitorPresetsLabel, service.MonitorPresets, DisplayControls.HardwareGroup, "Monitor presets", "monitor hardware ddc"), page?.MonitorPresetChoice);
    }

    private static SearchCard PresetsCard(string label, IPresetProvider provider, string group, string title, string keywords) =>
        new(label, $"{keywords} presets manage save apply preset cogwheel settings", group, title)
        {
            Choices = provider.Entries.Select(e => e.Name).Prepend(ProviderChoice.CustomText).ToList(),
            Menu = new[] { ProviderChoice.ManageText },
        };

    /// The page's search words (<c>FeaturePage.Words</c>), read without making the page; only the group cards while the service does not run.
    public static IEnumerable<string> Words(DisplaysService? service) => Cards(service, null).Words;
}
