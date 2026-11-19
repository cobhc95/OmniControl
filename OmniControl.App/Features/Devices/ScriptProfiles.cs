using System.Text.Json.Serialization;

namespace OmniControl.App.Features.Devices;

/// A template of the scripts method ("profile" in code, "template" on screen): how it talks to one kind of
/// device. Data on the master list: the commands for On, Off and reading the state (placeholders of <see cref="KasaCommand"/>), whether it needs
/// a sign-in and what to type there, its own set-up stages, known-error tips and the icon a new device gets. A built-in one ships with the app
/// (<see cref="ScriptProfiles.BuiltIn"/>, updated with it); the user's own are saved in devices.json and never overwritten.
public sealed class ScriptProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// A built-in profile's version (shown as "built-in · vN"); 0 for the user's own.
    public int Version { get; set; }

    public string On { get; set; } = string.Empty;
    public string Off { get; set; } = string.Empty;

    /// The command that asks the device's state; empty when there is none (Toggle then follows the last command).
    public string State { get; set; } = string.Empty;

    /// The icon (a library id, <see cref="Icons.IconLibrary"/>) a device on this profile shows until the user picks another.
    public string Icon { get; set; } = Icons.IconLibrary.Plug;

    /// The user's own profile: the built-in one it was made from (Reset to built-in goes back to it); empty when not known.
    public string BasedOn { get; set; } = string.Empty;

    /// The menu's muted line for this template, and its small tag ("slower"; empty = none).
    public string Brief { get; set; } = string.Empty;
    public string MenuTag { get; set; } = string.Empty;

    /// The device needs an account to sign in with; false hides the Sign-in stage and row.
    public bool NeedsSignIn { get; set; } = true;

    /// What to type in the sign-in (the email box's watermark).
    public string SignInNote { get; set; } = string.Empty;

    /// The device compares the email exactly, so it is handed to kasa in lower case (the field keeps what was typed).
    public bool LowerCaseEmail { get; set; }

    /// The template's own set-up stages, shown after Fixed address.
    public List<TemplateStage> Stages { get; set; } = new();

    /// Known errors: when kasa's answer holds <see cref="ErrorTip.Match"/>, the tip is shown instead of kasa's own line.
    public List<ErrorTip> Tips { get; set; } = new();

    [JsonIgnore] public bool IsBuiltIn => Version > 0;

    public string TemplateOf(DevicePower power) => power == DevicePower.On ? On : Off;

    /// A copy as the user's own template (a new id).
    public ScriptProfile AsOwn(string name) => new()
    {
        Name = name, On = On, Off = Off, State = State, Icon = Icon, BasedOn = IsBuiltIn ? Id : BasedOn, Brief = Brief, MenuTag = MenuTag, NeedsSignIn = NeedsSignIn, SignInNote = SignInNote, LowerCaseEmail = LowerCaseEmail,
        Stages = Stages.Select(s => s with { }).ToList(), Tips = Tips.Select(t => t with { }).ToList(),
    };
}

/// One extra set-up stage of a template: its id (kept per device when confirmed), title, the instruction and the confirm pill's words.
public sealed record TemplateStage(string Id = "", string Title = "", string Text = "", string Confirm = "");

/// A known error: kasa's answer holds <see cref="Match"/> (any case); <see cref="Tip"/> says what to do ({ip} is the device's address).
public sealed record ErrorTip(string Match = "", string Tip = "");

/// The master list of built-in templates; a device on one of them follows it when the app updates.
public static class ScriptProfiles
{
    public const string Auto = "auto", Tapo = "tapo", Kasa = "kasa";

    private const string Host = "kasa --host {ip}";

    // Tapo devices speak kasa's "smart" protocol; kasa works out the encryption itself.
    private const string Smart = Host + " --type smart";

    public static IReadOnlyList<ScriptProfile> BuiltIn { get; } = new[]
    {
        new ScriptProfile
        {
            Id = Tapo, Name = "Tapo", Version = 1, On = $"{Smart} on", Off = $"{Smart} off", State = $"{Smart} state", Icon = Icons.IconLibrary.Plug,
            Brief = "All Tapo plugs, bulbs, strips and switches. Needs Third-Party Compatibility.",
            SignInNote = "Tapo account email and password", LowerCaseEmail = true,
            Stages = { new TemplateStage("tapo-app", "Tapo app setting", "In the Tapo app: Me › Third-Party Services › Third-Party Compatibility → On. Without it the device refuses the sign-in.", "I've turned it on") },
            Tips =
            {
                new ErrorTip("challenge", "The device refused the sign-in. Turn on Third-Party Compatibility in the Tapo app (Me › Third-Party Services)."),
                new ErrorTip("Timed out", NoAnswer),
                new ErrorTip("Unable to connect", NoAnswer),
            },
        },
        new ScriptProfile { Id = Kasa, Name = "Kasa", Version = 1, On = $"{Host} on", Off = $"{Host} off", State = $"{Host} state", Icon = Icons.IconLibrary.Plug, NeedsSignIn = false, LowerCaseEmail = true,
            Brief = "Older Kasa plugs and bulbs. No sign-in.",
            Tips = { new ErrorTip("Timed out", NoAnswer), new ErrorTip("Unable to connect", NoAnswer) } },
        new ScriptProfile { Id = Auto, Name = "Auto-detect", Version = 1, On = $"{Host} on", Off = $"{Host} off", State = $"{Host} state", Icon = Icons.IconLibrary.Plug,
            Brief = "Any python-kasa device; finds it on the network.", MenuTag = "slower",
            SignInNote = "Account email and password (only for devices that need them)" },
    };

    private const string NoAnswer = "No answer from {ip}. Check the address in the Tapo app (device › Device info) and that the PC is on the same network.";

    // Ids of the earlier built-in profiles; a device on one follows its replacement.
    private static readonly Dictionary<string, string> Renamed = new() { ["tapo-plug"] = Tapo, ["tapo-bulb"] = Tapo, ["kasa-plug"] = Kasa };

    /// The template with this id among the built-in ones and the user's; Auto-detect when it is not known (a deleted template).
    /// Whether the email goes to the device in lower case: the template asks, or the built-in one it is a copy of does (copies saved before the flag existed).
    public static bool LowersEmail(ScriptProfile? profile) =>
        profile is not null && (profile.LowerCaseEmail || Of(profile.BasedOn, Array.Empty<ScriptProfile>()).LowerCaseEmail);

    public static ScriptProfile Of(string? id, IEnumerable<ScriptProfile> own)
    {
        if (id is not null && Renamed.TryGetValue(id, out var now)) id = now;
        return BuiltIn.FirstOrDefault(p => p.Id == id) ?? own.FirstOrDefault(p => p.Id == id) ?? BuiltIn[^1];
    }
}
