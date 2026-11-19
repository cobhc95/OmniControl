using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniControl.App.Features.Devices;

/// One smart device the user added: one name, icon and auto-check, and its methods in the order
/// they are tried (<see cref="MethodRecord"/>, each with its own set-up). Passwords are never part of it, so a backup of devices.json holds no secret.
public sealed class DeviceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// The id the device had on the PC it was imported from (empty = made here), so a second import knows it is already here.
    public string OriginId { get; set; } = string.Empty;

    /// The icon (a library id or a user icon, <see cref="Icons.IconLibrary"/>; empty = the first method's template's, or the plug), its colour while on, and the user's own image (a file in <see cref="DeviceIcons.ImageFolder"/>; empty = none).
    public string Icon { get; set; } = string.Empty;
    public string IconColour { get; set; } = string.Empty;
    public string IconImage { get; set; } = string.Empty;

    /// This device's own choice, used only while "Same for every device" is off; null = the shared one.
    public AutoCheck? AutoCheck { get; set; }

    /// This device's own "Also when the tray panel opens", used only while "Same for every device" is off; null = the shared one.
    public bool? AutoCheckOnTray { get; set; }

    /// The ways to reach it, in the order On, Off, Toggle and Check try them (the first that works is used). Never empty once loaded.
    public List<MethodRecord> Methods { get; set; } = new();

    /// At least one method's set-up is finished: it shows with a switch in the list.
    [JsonIgnore]
    public bool IsReady => Methods.Any(m => m.IsReady);

    /// A method's name in this device: its kind, numbered when the device has more than one of that kind ("Scripts 2").
    public string NameOf(MethodRecord method)
    {
        var same = Methods.Where(m => m.Kind == method.Kind).ToList();
        return same.Count > 1 ? $"{method.Kind} {same.IndexOf(method) + 1}" : method.Kind;
    }

    /// A device with no method gets one, which takes the device's id (so a password kept under the id still fits). True when one was added.
    public bool EnsureMethod()
    {
        if (Methods.Count > 0) return false;
        Methods.Add(new MethodRecord { Id = Id });
        return true;
    }

    /// A full copy (the set-up's Cancel puts it back).
    public DeviceRecord Clone() => JsonSerializer.Deserialize<DeviceRecord>(JsonSerializer.Serialize(this))!;

    /// Takes every field of <paramref name="other"/> (the copy made when the set-up opened); the id stays.
    public void CopyFrom(DeviceRecord other)
    {
        (Name, OriginId, Icon, IconColour, IconImage, AutoCheck, AutoCheckOnTray) = (other.Name, other.OriginId, other.Icon, other.IconColour, other.IconImage, other.AutoCheck, other.AutoCheckOnTray);
        Methods = other.Methods.Select(m => m.Clone()).ToList();
    }
}

/// A way to reach a device: its name, the two facts its menu row shows as tags, its brief (the row's muted line), its tooltip, and the two
/// words a device's method list shows at a row's right: how fast it is and where it works.
public sealed record DeviceMethod(string Name, IReadOnlyList<TagItem> Tags, string Brief, string Tip, string Speed, string Where);

/// The master list of ways to reach a device; a new one is one more entry here, shown only once it is built.
public static class DeviceMethods
{
    public const string Scripts = "Scripts", Simulation = "Simulation";

    public static IReadOnlyList<DeviceMethod> All { get; } = new[]
    {
        new DeviceMethod(Scripts, new[] { new TagItem("light", TagTone.Good), new TagItem("home Wi-Fi only") }, "Fast, exact on/off. Tapo and Kasa.",
            "Scripts: OmniControl runs python-kasa's command line to switch the plug.", "~1 s", "home Wi-Fi"),
        new DeviceMethod(Simulation, new[] { new TagItem("2–3 GB", TagTone.Caution, "The emulator holds this much memory while it runs."), new TagItem("works away", TagTone.Good) },
            "Any app, via the cloud. Slower; re-teach after app redesigns.",
            "Simulation: OmniControl taps through the device's own Android app in an emulator, the way you would, with steps you record.", "slower", "works away"),
    };

    /// The method stored in a file, or the scripts method when it names one that is not known.
    public static string Known(string? stored) => All.Any(m => m.Name == stored) ? stored! : Scripts;

    /// One sentence saying what the method does (the chip's tooltip and the menu row's tooltip).
    public static string Tip(string method) => Of(method).Tip;

    public static DeviceMethod Of(string method) => All.First(m => m.Name == Known(method));
}

/// How long the simulation method's emulator stays up (<see cref="EmulatorKeeper"/>).
public enum EmulatorKeep { Always, UntilIdle, AfterEachAction }

/// The emulator's keep-running settings, one set for the PC (every simulated device shares its emulator).
public sealed class EmulatorKeepSettings
{
    public EmulatorKeep Keep { get; set; } = EmulatorKeep.UntilIdle;

    /// Minutes without an action before <see cref="EmulatorKeep.UntilIdle"/> closes the emulator.
    public int IdleMinutes { get; set; } = Timing.EmulatorIdleMinutes;

    /// Wake the emulator when OmniControl starts (OmniControl itself then starts with Windows).
    public bool StartWithWindows { get; set; }

    /// The last cold boot finished, so the next start may load the saved snapshot (quick boot).
    public bool ColdBootWorked { get; set; }
}

/// The saved devices, in %APPDATA%\OmniControl\devices.json. Loading never runs anything.
public sealed class DeviceSettings
{
    public List<DeviceRecord> Devices { get; set; } = new();

    public EmulatorKeepSettings Emulator { get; set; } = new();

    /// The auto-check every device follows while <see cref="AutoCheckSame"/> is on (otherwise it is each device's fallback).
    public AutoCheck AutoCheck { get; set; } = AutoChecks.Default;

    /// "Also when the tray panel opens": on by default; follows <see cref="AutoCheckSame"/> like <see cref="AutoCheck"/>.
    public bool AutoCheckOnTray { get; set; } = true;

    /// "Same for every device": on by default; off = a pick changes one device only.
    public bool AutoCheckSame { get; set; } = true;

    /// The user's own scripts templates (the built-in ones are in <see cref="ScriptProfiles.BuiltIn"/>, never here).
    public List<ScriptProfile> Profiles { get; set; } = new();

    private static readonly DataStore Store = DataStores.Devices;

    public static DeviceSettings Load() => AppDataStore.Load<DeviceSettings>(Store) ?? new();

    public void Save() => AppDataStore.Save(Store, this);
}
