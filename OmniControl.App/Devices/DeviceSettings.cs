using System.Text.Json;
using System.Text.Json.Serialization;
using OmniControl.App.Services;

namespace OmniControl.App.Devices;

/// <summary>
/// One smart device the user added (docs/mockups/device-multiple-methods.html): one name, icon and auto-check, and its methods in the order
/// they are tried (<see cref="MethodRecord"/>, each with its own set-up). Passwords are never part of it, so a backup of devices.json holds no secret.
/// </summary>
public sealed class DeviceRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// <summary>The icon (a <see cref="DeviceIcons"/> id; empty = the first method's template's, or the plug), its colour while on, and the user's own image (a file in <see cref="DeviceIcons.ImageFolder"/>; empty = none).</summary>
    public string Icon { get; set; } = string.Empty;
    public string IconColour { get; set; } = string.Empty;
    public string IconImage { get; set; } = string.Empty;

    /// <summary>When the real state is read on its own; null = <see cref="AutoChecks.DefaultFor"/> the first method.</summary>
    public AutoCheck? AutoCheck { get; set; }

    /// <summary>The ways to reach it, in the order On, Off, Toggle and Check try them (the first that works is used). Never empty once loaded.</summary>
    public List<MethodRecord> Methods { get; set; } = new();

    /// <summary>The fields of a record saved before a device had several methods (they were its one method's); read once by <see cref="Migrate"/>.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Legacy { get; set; }

    /// <summary>At least one method's set-up is finished: it shows with a switch in the list.</summary>
    [JsonIgnore]
    public bool IsReady => Methods.Any(m => m.IsReady);

    /// <summary>The kind of its first method (what the auto-check default and the default icon follow).</summary>
    [JsonIgnore]
    public string FirstKind => Methods.FirstOrDefault()?.Kind ?? DeviceMethods.Scripts;

    /// <summary>A method's name in this device: its kind, numbered when the device has more than one of that kind ("Scripts 2").</summary>
    public string NameOf(MethodRecord method)
    {
        var same = Methods.Where(m => m.Kind == method.Kind).ToList();
        return same.Count > 1 ? $"{method.Kind} {same.IndexOf(method) + 1}" : method.Kind;
    }

    /// <summary>
    /// A record saved before several methods existed becomes a one-method device: its old fields are that method's and the method takes the
    /// device's id, so its password (kept under the id) and its shortcuts still fit. A record that already has methods drops any old fields
    /// (an old backup merged into a newer file). A device with no method gets one. True when anything changed.
    /// </summary>
    public bool Migrate()
    {
        var had = Legacy is { Count: > 0 };
        if (Methods.Count == 0)
        {
            var method = had ? JsonSerializer.Deserialize<MethodRecord>(JsonSerializer.Serialize(Legacy))! : new MethodRecord();
            if (Legacy?.TryGetValue(LegacyMethodField, out var kind) == true && kind.ValueKind == JsonValueKind.String) method.Kind = kind.GetString()!;
            method.Id = Id;
            Methods.Add(method);
            had = true;
        }
        Legacy = null;
        return had;
    }

    /// <summary>The old one-method record's name for the method's kind.</summary>
    private const string LegacyMethodField = "Method";

    /// <summary>A full copy (the set-up's Cancel puts it back).</summary>
    public DeviceRecord Clone() => JsonSerializer.Deserialize<DeviceRecord>(JsonSerializer.Serialize(this))!;

    /// <summary>Takes every field of <paramref name="other"/> (the copy made when the set-up opened); the id stays.</summary>
    public void CopyFrom(DeviceRecord other)
    {
        (Name, Icon, IconColour, IconImage, AutoCheck) = (other.Name, other.Icon, other.IconColour, other.IconImage, other.AutoCheck);
        Methods = other.Methods.Select(m => m.Clone()).ToList();
    }
}

/// <summary>
/// A way to reach a device: its name, the two facts its menu row shows as tags, its brief (the row's muted line), its tooltip, and the two
/// words a device's method list shows at a row's right: how fast it is and where it works.
/// </summary>
public sealed record DeviceMethod(string Name, IReadOnlyList<TagItem> Tags, string Brief, string Tip, string Speed, string Where);

/// <summary>The master list of ways to reach a device (docs/mockups/method-menu-briefs.html); a new one is one more entry here, shown only once it is built.</summary>
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

    /// <summary>The method stored in a file, or the scripts method when it names one that is not known.</summary>
    public static string Known(string? stored) => All.Any(m => m.Name == stored) ? stored! : Scripts;

    /// <summary>One sentence saying what the method does (the chip's tooltip and the menu row's tooltip).</summary>
    public static string Tip(string method) => Of(method).Tip;

    public static DeviceMethod Of(string method) => All.First(m => m.Name == Known(method));
}

/// <summary>How long the simulation method's emulator stays up (<see cref="EmulatorKeeper"/>).</summary>
public enum EmulatorKeep { Always, UntilIdle, AfterEachAction }

/// <summary>The emulator's keep-running settings, one set for the PC (every simulated device shares its emulator).</summary>
public sealed class EmulatorKeepSettings
{
    public EmulatorKeep Keep { get; set; } = EmulatorKeep.UntilIdle;

    /// <summary>Minutes without an action before <see cref="EmulatorKeep.UntilIdle"/> closes the emulator.</summary>
    public int IdleMinutes { get; set; } = Timing.EmulatorIdleMinutes;

    /// <summary>Wake the emulator when OmniControl starts (OmniControl itself then starts with Windows).</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>The last cold boot finished, so the next start may load the saved snapshot (quick boot).</summary>
    public bool ColdBootWorked { get; set; }
}

/// <summary>The saved devices, in %APPDATA%\OmniControl\devices.json. Loading never runs anything.</summary>
public sealed class DeviceSettings
{
    public List<DeviceRecord> Devices { get; set; } = new();

    public EmulatorKeepSettings Emulator { get; set; } = new();

    /// <summary>The user's own scripts templates (the built-in ones are in <see cref="ScriptProfiles.BuiltIn"/>, never here).</summary>
    public List<ScriptProfile> Profiles { get; set; } = new();

    private static readonly DataStore Store = DataStores.Devices;

    public static DeviceSettings Load() => AppDataStore.Load<DeviceSettings>(Store) ?? new();

    public void Save() => AppDataStore.Save(Store, this);
}
