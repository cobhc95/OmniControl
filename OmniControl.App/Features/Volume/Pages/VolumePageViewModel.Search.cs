using OmniControl.App.Features.Volume.Audio;
using OmniControl.App.Features.Volume.Pages.Easy;

namespace OmniControl.App.Features.Volume.Pages;

// What the Volume page offers to global search, defined once (see SearchBuilder): the tabs and views, the group tips, the Plays on, Records
// from and Method menus (the real devices and routes), the effects, the Easy goals, one result per row, Wiring row and cable, each with its
// live row when the page is made.
public sealed partial class VolumePageViewModel
{
    private const string PageName = PageTitles.Volume, VolumeWords = "sound audio mixer";

    private const string OutputsTip = "Plays on and Method open their menus; FX opens and closes the row's FX window. Programs with settings are remembered after they close; × forgets one.";
    private const string CablesTip = "Virtual cables found on this PC. OmniControl uses only the ones you switch on, so a cable another program uses is left alone.";
    private const string WiringTip = "A chip's × removes a device, + add adds one. A dimmed choice says why in its tooltip.";

    private static readonly SearchCard CombineCard = new("Combine devices", "combine merge speakers microphones mic mics mix several devices together cable stereo mix", WiringTab)
        {
            Choices = new[] { CombineSpeakersText, CombineMicrophonesText },
        },
        ApoCard = new(EqualizerApoGroup.StatusLabel, "equalizer apo eq effects install remove device selector attach speakers microphone driver", WiringTab, "Equalizer APO"),
        MicrophonesCard = new(EffectsRules.UseMicrophonesLabel, VolumeControls.UseMicrophonesKeywords, MixerTab) { Tip = EffectsRules.UseMicrophonesTip },
        TabsCard = new("Volume tabs and views", VolumeWords + " easy advanced view switch", Title: PageName)
        {
            Choices = new[] { MixerTab, WiringTab }.Concat(Enum.GetNames<VolumeView>()).ToList(),
        },
        OutputsGroupCard = new("Outputs", VolumeWords + " master programs apps combined speakers show closed add program", MixerTab) { Tip = OutputsTip },
        CablesGroupCard = new("Virtual cables", CablesKeywords + " get more cables", WiringTab, CableGroup.GroupName) { Tip = CablesTip },
        WiringGroupCard = new("Wiring", VolumeWords + " wiring route outputs microphones", WiringTab) { Tip = WiringTip },
        MethodCard = new("Method", VolumeWords + " effects fx route how run", MixerTab)
        {
            Menu = EffectsRules.ProgramRoutes.Concat(EffectsRules.MasterRoutes).SelectMany(r => new[] { r.Name, r.Description }).Distinct().ToList(),
            Tip = EffectsRules.MethodOffLine,
        },
        EffectsCard = new("Effects", VolumeWords + " fx chain add effect plug-in", MixerTab)
        {
            Choices = FxCatalog.All.Select(k => k.Name).Concat(FxCatalog.PluginTypes.Select(t => t.Description)).ToList(),
        },
        GoalsCard = new("Easy goals", VolumeWords + " easy welcome set up guided", Title: PageName) { Choices = EasyGoals.All.Select(g => g.Title).ToList() };

    private static SearchCard RowCard(string name) =>
        new(name, VolumeWords + " app program pan fx effects method cable capture replay recording output device microphone", MixerTab, PageName);

    private static SearchCard WiringCard(string name, bool input) => new(name,
        "wiring route plays on records from output device speakers headphones microphone combine combined mix mics several cable", WiringTab, input ? RecordsFromWords : PlaysOnWords);

    private static SearchCard CableCard(string name) => new(name, CablesKeywords, WiringTab, CableGroup.GroupName);

    // The Plays on and Records from menus offer the devices the mixer lists now.
    private static SearchCard PlaysOnCard(MixerService? mixer) =>
        new(PlaysOnWords, VolumeWords + " output device speakers headphones route", MixerTab) { Menu = (mixer?.Devices.Select(d => d.Name) ?? Enumerable.Empty<string>()).Prepend(FollowsMasterName).ToList() };

    private static SearchCard RecordsFromCard(MixerService? mixer) =>
        new(RecordsFromWords, VolumeWords + " microphone input device record route", MixerTab) { Menu = (mixer?.RecordingDevices.Select(d => d.Name) ?? Enumerable.Empty<string>()).ToList() };

    protected override SearchBuilder SearchCards => Cards(_mixer, _support, this);

    /// THE definition of the page's search: the tabs, groups and menus, then every row of the mixer (the master, the programs, the microphones
    /// and combined devices, each also a Wiring row) and the cables the effects support found, each with its live row when
    /// <paramref name="page"/> is given.
    private static SearchBuilder Cards(MixerService? mixer, EffectsSupportService? support, VolumePageViewModel? page)
    {
        var builder = new SearchBuilder().Add(TabsCard).Add(OutputsGroupCard).Add(CablesGroupCard).Add(WiringGroupCard).Add(MethodCard).Add(EffectsCard).Add(GoalsCard)
            .Add(PlaysOnCard(mixer)).Add(RecordsFromCard(mixer))
            .Add(CombineCard, page?._combineCard).Add(ApoCard, page?.Apo.Status).Add(MicrophonesCard, page?.UseMicrophones);
        IReadOnlyList<string> names = mixer is null ? Array.Empty<string>() : mixer.Settings.Apps.Select(a => a.Name)
            .Concat(mixer.RecordingDevices.Select(d => d.Name)).Concat(mixer.Combined.Select(c => c.Name)).Prepend(MasterName).Distinct().ToList();
        var cables = (support?.Current?.Found.Select(f => f.Name) ?? Enumerable.Empty<string>()).Concat(page?.WiringCables.Items.Select(c => c.Name) ?? Enumerable.Empty<string>()).Distinct();
        return builder
            .AddEach(names, RowCard, n => page?.AllRows().FirstOrDefault(r => r.Name == n))
            .AddEach(names, n => WiringCard(n, false), n => page?.WiringRows().FirstOrDefault(w => w.Source.Name == n && !page.WiringInputs.Items.Contains(w)))
            .AddEach(names, n => WiringCard(n, true), n => page?.WiringRows().FirstOrDefault(w => w.Source.Name == n && page.WiringInputs.Items.Contains(w)))
            .AddEach(cables, CableCard, n => page?.WiringCables.Items.FirstOrDefault(c => c.Name == n));
    }

    /// The page's search words (<c>FeaturePage.Words</c>), read without making the page.
    public static IEnumerable<string> Words(MixerService? mixer, EffectsSupportService? support) => Cards(mixer, support, null).Words;
}
