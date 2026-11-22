using System.Diagnostics;
using System.Text.Json;
using OmniControl.App.Shared.Scripts;
using OmniControl.Core.Interfaces;

namespace OmniControl.App.Features.Devices;

/// How a scripts command ended: the problem line (null when it worked), what the program printed, and the masked log lines for "Copy details".
public sealed record ScriptRun(string? Problem, string Printed, string Details);

/// The scripts method: the device's template's command templates run through <see cref="ScriptRunner"/> as the normal user, hidden, stopped
/// after Timing.DeviceCommandTimeout; switching, reading the state and the user's own templates. The email and password reach kasa in
/// environment variables, never in the arguments; everything logged or copied has both masked.
public sealed class ScriptMethod
{
    private const string Masked = "•••";

    private readonly DeviceSettings _settings;
    private readonly DeviceSetup _setup;
    private readonly ICredentialStore _credentials;
    private readonly Action _edited;

    /// <param name="edited">Saves the device list after a template was picked or added.</param>
    internal ScriptMethod(DeviceSettings settings, DeviceSetup setup, ICredentialStore credentials, Action edited)
    {
        _settings = settings;
        _setup = setup;
        _credentials = credentials;
        _edited = edited;
    }

    public ScriptProfile ProfileOf(MethodRecord method) => ScriptProfiles.Of(method.ProfileId, _settings.Profiles);

    internal async Task<Attempt> RunAsync(MethodRecord method, DevicePower power, bool? lastKnown, ScriptProfile profile, CancellationToken stop)
    {
        var turnOn = DeviceService.Wanted(power) ?? (await ReadStateAsync(method, profile, stop) ?? lastKnown) != true;
        var clock = Stopwatch.StartNew();
        var run = await RunTemplateAsync(method, profile.TemplateOf(turnOn ? DevicePower.On : DevicePower.Off), profile, stop);
        if (run.Problem is not null) return Attempt.Failed(run.Problem, run.Details);
        var reads = await ReadStateAsync(method, profile, stop);
        clock.Stop();
        if (reads is { } state && state != turnOn) return Attempt.Failed($"it still reads {(state ? "on" : "off")} after the command.", run.Details);
        var words = $"turned {(turnOn ? "on" : "off")} in {clock.Elapsed.TotalSeconds:0.0} s{(reads is null ? string.Empty : ", read back")}";
        return new Attempt(null, turnOn, words, reads is not null, run.Details);
    }

    /// The device's state by the profile's State command; null when it has none or its answer does not say.
    private async Task<bool?> ReadStateAsync(MethodRecord method, ScriptProfile profile, CancellationToken stop)
    {
        if (profile.State.Trim().Length == 0) return null;
        var run = await RunTemplateAsync(method, profile.State, profile, stop);
        return run.Problem is null ? KasaCommand.StateOf(run.Printed) : null;
    }

    /// Reads the state for a check: a failure says why (no State command, the command failed, its answer does not say).
    internal async Task<Attempt> CheckAsync(MethodRecord method)
    {
        var profile = ProfileOf(method);
        if (profile.State.Trim().Length == 0) return Attempt.Failed("its template has no State command to read with.");
        var run = await RunTemplateAsync(method, profile.State, profile);
        if (run.Problem is not null) return Attempt.Failed(run.Problem, run.Details);
        if (KasaCommand.StateOf(run.Printed) is not { } on) return Attempt.Failed("its answer didn't say whether it is on or off.", run.Details);
        return new Attempt(null, on, Details: run.Details);
    }

    /// Runs one command template for the method (its address and sign-in): <c>kasa</c> is the kasa program found, the placeholders are filled in; a known error of <paramref name="profile"/> is told by its tip.
    public async Task<ScriptRun> RunTemplateAsync(MethodRecord method, string template, ScriptProfile? profile = null, CancellationToken stop = default)
    {
        var runsKasa = KasaCommand.RunsKasa(template); // the account goes only to kasa; a template of any other program runs without it
        var email = runsKasa ? KasaCommand.EmailFor(method.Email, ScriptProfiles.LowersEmail(profile)) : string.Empty;
        var words = KasaCommand.Fill(template, method.Address.Trim(), email);
        if (words.Count == 0) return new ScriptRun("the command is empty", string.Empty, string.Empty);
        if (words[0].Equals(KasaCommand.Program, StringComparison.OrdinalIgnoreCase) && _setup.Kasa is null) return new ScriptRun("python-kasa is not installed", string.Empty, string.Empty);
        var (file, arguments) = words[0].Equals(KasaCommand.Program, StringComparison.OrdinalIgnoreCase)
            ? (_setup.Kasa!.Value.File, _setup.Kasa.Value.Lead.Concat(words.Skip(1)).ToList())
            : (words[0], words.Skip(1).ToList());
        var password = runsKasa ? _credentials.Read(DeviceService.PasswordTarget(method)) ?? string.Empty : string.Empty;
        var environment = runsKasa
            ? new Dictionary<string, string> { [KasaCommand.UserVariable] = email, [KasaCommand.PasswordVariable] = password }
            : null;
        var printed = string.Empty;
        var problem = await ScriptRunner.RunAsync(file, arguments, hidden: true, Timing.DeviceCommandTimeout, environment, text => printed = text, stop);
        var details = Mask(method, password, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {file} {string.Join(' ', arguments)}\n{problem ?? "ended with code 0"}\n{printed}".Trim());
        DeviceService.Log(details.ReplaceLineEndings(" | "));
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

    /// Saves <paramref name="draft"/> (the templates of <paramref name="from"/>, perhaps edited) as a new template of the user's ("My Tapo") and puts the device on it.
    public ScriptProfile SaveAsOwn(MethodRecord method, ScriptProfile draft, ScriptProfile from)
    {
        var name = $"My {from.Name}";
        for (var n = 2; _settings.Profiles.Any(p => p.Name == name); n++) name = $"My {from.Name} {n}";
        var own = draft.AsOwn(name);
        own.BasedOn = from.IsBuiltIn ? from.Id : from.BasedOn;
        _settings.Profiles.Add(own);
        method.ProfileId = own.Id;
        _edited();
        return own;
    }

    /// Puts the device back on the built-in template its own one was made from (Auto-detect when it is not known).
    public void ResetToBuiltIn(MethodRecord method)
    {
        method.ProfileId = ProfileOf(method) is { IsBuiltIn: false } own ? ScriptProfiles.Of(own.BasedOn, Array.Empty<ScriptProfile>()).Id : method.ProfileId;
        _edited();
    }

    /// Writes a template to a file the user picked. Null when it worked, otherwise one plain sentence.
    public string? ExportProfile(ScriptProfile profile, string path)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(profile.AsOwn(profile.Name), new JsonSerializerOptions { WriteIndented = true }));
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DeviceService.Log($"Exporting the template {profile.Name}: {ex.Message}");
            return "The template couldn't be written there.";
        }
    }

    /// Reads a template file as a new template of the user's and puts the device on it. The problem is one plain sentence.
    public (ScriptProfile? Profile, string? Problem) ImportProfile(MethodRecord method, string path)
    {
        try
        {
            if (JsonSerializer.Deserialize<ScriptProfile>(File.ReadAllText(path)) is not { } read || read.On.Trim().Length == 0 || read.Off.Trim().Length == 0)
                return (null, "That file holds no template.");
            var own = read.AsOwn(read.Name.Trim().Length > 0 ? read.Name.Trim() : Path.GetFileNameWithoutExtension(path));
            _settings.Profiles.Add(own);
            method.ProfileId = own.Id;
            _edited();
            return (own, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DeviceService.Log($"Importing the template {path}: {ex.Message}");
            return (null, "That file couldn't be read as a template.");
        }
    }
}
