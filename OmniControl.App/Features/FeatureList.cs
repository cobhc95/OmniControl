using OmniControl.Core.Engine;

namespace OmniControl.App.Features;

/// One feature the person may switch off (App settings > Features, Setup's "Choose features" page).
/// <param name="Id">Its id, kept in settings.json and used as Setup's component name (tools/setup.iss retypes them).</param>
/// <param name="Live">True: switching it starts or stops it at once; false: from the next start (its card says so).</param>
/// <param name="File">The file Setup leaves out with it (<see cref="OptionalFiles"/>); while it is missing the feature is not installed. Null: always installed.</param>
/// <param name="Driver">The driver it installs on the PC, which stays there while the feature is off; null: none.</param>
public sealed record FeatureInfo(string Id, string Name, string Description, bool Live, string? File = null, string? Driver = null);

/// THE list of features that can be switched off, in the order the switches show, and the ready-made choices (the same as Setup's types).
/// Where a feature runs and shows is the module that names its id in <see cref="IFeatureModule.Features"/> (one registration); a module that
/// names none always runs.
public static class FeatureList
{
    public const string Volume = "volume", Displays = "displays", Input = "input", Automation = "automation", Osd = "osd", Resources = "resources",
        Traffic = "traffic", OtherPcs = "otherpcs", Devices = "devices", Screenshot = "screenshot";

    public static readonly IReadOnlyList<FeatureInfo> All = new FeatureInfo[]
    {
        // Restart: the audio engine's plug-ins may hang while unloading, and Input, Automation and App settings hold the mixer.
        new(Volume, "Sound and effects", "Volume, mixer, effects (Equalizer APO)", Live: false, OptionalFiles.EqualizerApo, "Equalizer APO"),
        // Restart: the monitors' engine and the software displays are made first and given back only at exit.
        new(Displays, "Displays", "Brightness, colour, rotation", Live: false),
        // Restart: the one mouse hook and the pointer settings' kept button swap.
        new(Input, "Mouse and trackpad", "Button remapping, gestures", Live: false),
        new(Automation, "Hotkeys and automation", "Keys, rules, custom actions", Live: true),
        new(Osd, "On-screen display", "OSD pop-ups", Live: true),
        new(Resources, "Resources and taskbar gadget", "CPU, GPU, network, temperatures", Live: true, OptionalFiles.Sensors),
        new(Traffic, OmniControl.App.Features.Traffic.Pages.TrafficPageViewModel.PageTitle, "Per-app limits (Windows Packet Filter)", Live: true, OptionalFiles.PacketFilter, "Windows Packet Filter"),
        // Restart: Smart devices' import and App settings' import from a PC hold its paired PCs.
        new(OtherPcs, "Other PCs", "Pairing, wake, remote control", Live: false),
        new(Devices, "Smart devices", "Plugs and bulbs", Live: true),
        new(Screenshot, "Screenshot tool", "Screenshots and screen recording", Live: true, OptionalFiles.QrCodes),
    };

    public static FeatureInfo? Find(string id) => All.FirstOrDefault(f => f.Id == id);

    /// The ready-made choices, by the features each leaves on; a choice that matches none is <see cref="Custom"/>.
    public static readonly IReadOnlyList<(string Name, IReadOnlyList<string> On)> ReadyMade = new (string, IReadOnlyList<string>)[]
    {
        ("Everything", All.Select(f => f.Id).ToList()),
        ("Sound only", new[] { Volume }),
        ("Monitoring only", new[] { Resources }),
        ("None", Array.Empty<string>()),
    };

    public const string Custom = "Custom";

    /// The ready-made choice whose features are exactly those not in <paramref name="off"/>, else <see cref="Custom"/>.
    public static string ReadyMadeOf(IReadOnlyCollection<string> off) =>
        ReadyMade.FirstOrDefault(r => All.All(f => r.On.Contains(f.Id) != off.Contains(f.Id))).Name ?? Custom;

    /// The features a ready-made choice leaves off.
    public static List<string> OffIn(IReadOnlyList<string> on) => All.Select(f => f.Id).Where(id => !on.Contains(id)).ToList();

    /// Setup's comma-separated ids, only the known ones.
    public static List<string> Parse(string ids) => ids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Where(id => Find(id) is not null).Distinct().ToList();
}
