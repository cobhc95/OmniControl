using System.Diagnostics;
using System.Text.Json;
using OmniControl.App.Rules;
using OmniControl.App.Services;
using OmniControl.Core.Engine;

namespace OmniControl.App.Devices;

/// <summary>How a scripts command ended: the problem line (null when it worked), what the program printed, and the masked log lines for "Copy details".</summary>
public sealed record ScriptRun(string? Problem, string Printed, string Details);

// The scripts method (docs/mockups/script-templates.html): the device's template's command templates run through ScriptRunner as the normal
// user, hidden, stopped after Timing.DeviceCommandTimeout. The email and password reach kasa in environment variables, never in the
// arguments; everything logged or copied has both masked.
public sealed partial class DeviceService
{
    private const string Masked = "•••";

    /// <summary>Every template a device can use: the built-in ones, then the user's own.</summary>
    public IEnumerable<ScriptProfile> AllProfiles => ScriptProfiles.BuiltIn.Concat(Settings.Profiles);

    public ScriptProfile ProfileOf(MethodRecord method) => ScriptProfiles.Of(method.ProfileId, Settings.Profiles);

    private async Task<Attempt> RunScriptAsync(MethodRecord method, DevicePower power, bool? lastKnown, ScriptProfile profile)
    {
        var turnOn = power switch
        {
            DevicePower.On => true,
            DevicePower.Off => false,
            _ => (await ReadScriptStateAsync(method, profile) ?? lastKnown) != true,
        };
        var clock = Stopwatch.StartNew();
        var run = await RunTemplateAsync(method, profile.TemplateOf(turnOn ? DevicePower.On : DevicePower.Off), profile);
        if (run.Problem is not null) return Attempt.Failed(run.Problem, run.Details);
        var reads = await ReadScriptStateAsync(method, profile);
        clock.Stop();
        if (reads is { } state && state != turnOn) return Attempt.Failed($"it still reads {(state ? "on" : "off")} after the command.", run.Details);
        var words = $"turned {(turnOn ? "on" : "off")} in {clock.Elapsed.TotalSeconds:0.0} s{(reads is null ? string.Empty : ", read back")}";
        return new Attempt(null, turnOn, words, reads is not null, run.Details);
    }

    /// <summary>The device's state by the profile's State command; null when it has none or its answer does not say.</summary>
    private async Task<bool?> ReadScriptStateAsync(MethodRecord method, ScriptProfile profile)
    {
        if (profile.State.Trim().Length == 0) return null;
        var run = await RunTemplateAsync(method, profile.State, profile);
        return run.Problem is null ? KasaCommand.StateOf(run.Printed) : null;
    }

    /// <summary>
    /// Turns the device off, then on again, by this one method with <paramref name="profile"/> (the method's own, or the Advanced pane's
    /// draft). Null when it worked, otherwise one plain sentence; <c>Confirmed</c> when the State command read both changes back.
    /// </summary>
    public async Task<(string? Problem, bool Confirmed)> TestScriptAsync(DeviceRecord device, MethodRecord method, ScriptProfile? profile = null)
    {
        profile ??= ProfileOf(method);
        var problem = await RunAsync(device, DevicePower.Off, method, profile) ?? await RunAsync(device, DevicePower.On, method, profile);
        return (problem, problem is null && RuntimeOf(device).Confirmed);
    }

    /// <summary>Runs one command template for the method (its address and sign-in): <c>kasa</c> is the kasa program found, the placeholders are filled in; a known error of <paramref name="profile"/> is told by its tip.</summary>
    public async Task<ScriptRun> RunTemplateAsync(MethodRecord method, string template, ScriptProfile? profile = null)
    {
        var email = KasaCommand.EmailFor(method.Email, profile?.LowerCaseEmail ?? false);
        var words = KasaCommand.Fill(template, method.Address.Trim(), email);
        if (words.Count == 0) return new ScriptRun("the command is empty", string.Empty, string.Empty);
        if (words[0].Equals(KasaCommand.Program, StringComparison.OrdinalIgnoreCase) && Setup.Kasa is null) return new ScriptRun("python-kasa is not installed", string.Empty, string.Empty);
        var (file, arguments) = words[0].Equals(KasaCommand.Program, StringComparison.OrdinalIgnoreCase)
            ? (Setup.Kasa!.Value.File, Setup.Kasa.Value.Lead.Concat(words.Skip(1)).ToList())
            : (words[0], words.Skip(1).ToList());
        var password = _credentials.Read(PasswordTarget(method)) ?? string.Empty;
        var environment = new Dictionary<string, string>
        {
            [KasaCommand.UserVariable] = email,
            [KasaCommand.PasswordVariable] = password,
        };
        var printed = string.Empty;
        var problem = await ScriptRunner.RunAsync(file, arguments, hidden: true, Timing.DeviceCommandTimeout, environment, text => printed = text);
        var details = Mask(method, password, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {file} {string.Join(' ', arguments)}\n{problem ?? "ended with code 0"}\n{printed}".Trim());
        Log(details.ReplaceLineEndings(" | "));
        if (problem is null) return new ScriptRun(null, printed, details);
        var errorText = problem.Contains(": ", StringComparison.Ordinal) ? problem[(problem.IndexOf(": ", StringComparison.Ordinal) + 2)..] : string.Empty;
        var line = KasaCommand.ProblemLine($"{printed}\n{errorText}");
        var tip = profile?.Tips.FirstOrDefault(t => t.Match.Length > 0 && $"{printed} {errorText}".Contains(t.Match, StringComparison.OrdinalIgnoreCase));
        var said = tip is not null ? tip.Tip.Replace(KasaCommand.IpField, method.Address.Trim(), StringComparison.OrdinalIgnoreCase) : line.Length > 0 ? line : problem;
        return new ScriptRun(Mask(method, password, said), printed, details);
    }

    private static string Mask(MethodRecord method, string password, string text)
    {
        if (password.Length > 0) text = text.Replace(password, Masked, StringComparison.Ordinal);
        if (method.Email.Trim().Length > 0) text = text.Replace(method.Email.Trim(), Masked, StringComparison.OrdinalIgnoreCase);
        return text;
    }

    // ---- The user's own templates ----

    /// <summary>Saves <paramref name="draft"/> (the templates of <paramref name="from"/>, perhaps edited) as a new template of the user's ("My Tapo") and puts the device on it.</summary>
    public ScriptProfile SaveAsOwn(MethodRecord method, ScriptProfile draft, ScriptProfile from)
    {
        var name = $"My {from.Name}";
        for (var n = 2; Settings.Profiles.Any(p => p.Name == name); n++) name = $"My {from.Name} {n}";
        var own = draft.AsOwn(name);
        own.BasedOn = from.IsBuiltIn ? from.Id : from.BasedOn;
        Settings.Profiles.Add(own);
        method.ProfileId = own.Id;
        Edited();
        return own;
    }

    /// <summary>Puts the device back on the built-in template its own one was made from (Auto-detect when it is not known).</summary>
    public void ResetToBuiltIn(MethodRecord method)
    {
        method.ProfileId = ProfileOf(method) is { IsBuiltIn: false } own ? ScriptProfiles.Of(own.BasedOn, Array.Empty<ScriptProfile>()).Id : method.ProfileId;
        Edited();
    }

    /// <summary>Writes a template to a file the user picked. Null when it worked, otherwise one plain sentence.</summary>
    public string? ExportProfile(ScriptProfile profile, string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(profile.AsOwn(profile.Name), new JsonSerializerOptions { WriteIndented = true }));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Exporting the template {profile.Name}: {ex.Message}");
            return "The template couldn't be written there.";
        }
    }

    /// <summary>Reads a template file as a new template of the user's and puts the device on it. The problem is one plain sentence.</summary>
    public (ScriptProfile? Profile, string? Problem) ImportProfile(MethodRecord method, string path)
    {
        try
        {
            if (JsonSerializer.Deserialize<ScriptProfile>(File.ReadAllText(path)) is not { } read || read.On.Trim().Length == 0 || read.Off.Trim().Length == 0)
                return (null, "That file holds no template.");
            var own = read.AsOwn(read.Name.Trim().Length > 0 ? read.Name.Trim() : Path.GetFileNameWithoutExtension(path));
            Settings.Profiles.Add(own);
            method.ProfileId = own.Id;
            Edited();
            return (own, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log($"Importing the template {path}: {ex.Message}");
            return (null, "That file couldn't be read as a template.");
        }
    }
}
