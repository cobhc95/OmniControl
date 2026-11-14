using Avalonia.Threading;
using OmniControl.App.Audio;
using OmniControl.App.Pages;

namespace OmniControl.App.Fx;

/// <summary>What the preset manager needs to open for one effect: its kind and a way to read the effect's settings as they are now.</summary>
public sealed record PresetManagerRequest(FxKind Kind, Func<PresetContent> Snapshot);

/// <summary>Where the preset pull-down gets the user's presets and opens the manager (the effect chain; the window that shows it does the opening).</summary>
internal interface IPresetHost
{
    UserPresetStore Store { get; }

    /// <summary>Opens the preset manager on the kind and completes when it is closed.</summary>
    Task ManagePresetsAsync(PresetManagerRequest request);
}

/// <summary>How one effect reads, shows and keeps its settings as a preset; the effect's view-model supplies it.</summary>
internal sealed record PresetSource(FxKind Kind, Func<PresetContent> Read, Action<PresetContent> Write, Func<PresetContent?> GetCustom, Action<PresetContent?> SetCustom);

/// <summary>
/// The preset pull-down of any effect with presets (the three equalisers, the compressor), built once for all of them: the built-in
/// presets, the user's own (marked with a star), "Custom", and "Manage presets..." last. The shown name is always worked out from the
/// settings (the preset that matches them), so it is right after a restart without storing it. When a preset replaces hand-tuned settings
/// they are kept on the effect (<see cref="FxSlot.Custom"/>) and "Custom" brings them back.
/// </summary>
internal sealed class PresetPicker : IDisposable
{
    public const string CustomName = "Custom", ManageText = "Manage presets…", UserMark = "★ ";

    private readonly PresetSource _source;
    private readonly IPresetHost _host;
    private readonly Action _save;
    private readonly IReadOnlyList<(string Name, PresetContent Content)> _builtIn;
    private PresetContent _last;
    private bool _lastIsCustom;

    public PresetPicker(PresetSource source, IPresetHost host, Action save)
    {
        _source = source;
        _host = host;
        _save = save;
        _builtIn = PresetCatalog.BuiltIns(source.Kind);
        _last = source.Read();
        _lastIsCustom = NameFor(_last) == CustomName;
        Choice = new ChoiceItem("Preset", OptionList(), NameFor(_last), OnPick);
        host.Store.Changed += Refresh;
    }

    public ChoiceItem Choice { get; }

    /// <summary>The user changed the settings by hand (or a button such as Flat did): the shown name follows, and hand-tuned settings that a preset replaced are kept.</summary>
    public void Edited()
    {
        Track(_source.Read());
        Refresh();
    }

    public void Dispose() => _host.Store.Changed -= Refresh;

    private List<string> OptionList() => _builtIn.Select(p => p.Name)
        .Concat(_host.Store.Names(_source.Kind.Kind).Select(n => UserMark + n))
        .Append(CustomName).Append(ManageText).ToList();

    private string NameFor(PresetContent content)
    {
        if (_builtIn.FirstOrDefault(p => p.Content.Matches(content)) is { Name: { } name }) return name;
        return _host.Store.Of(_source.Kind.Kind).FirstOrDefault(p => p.Content.Matches(content)) is { } own ? UserMark + own.Name : CustomName;
    }

    /// <summary>Shows the list and name as they are now; the list object stays the same while its entries do, so the box is not reset by every edit.</summary>
    private void Refresh()
    {
        var options = OptionList();
        Choice.Load(options.SequenceEqual(Choice.Options) ? Choice.Options : options, NameFor(_source.Read()));
    }

    private void Track(PresetContent now)
    {
        var isCustom = NameFor(now) == CustomName;
        if (_lastIsCustom && !isCustom) _source.SetCustom(_last.Copy());
        (_last, _lastIsCustom) = (now, isCustom);
    }

    private void OnPick(string? name)
    {
        if (name is null) return; // the box lost its selection while its list was replaced
        if (name == ManageText)
        {
            _ = ManageAsync();
            return;
        }
        var content = name == CustomName ? _source.GetCustom()
            : name.StartsWith(UserMark, StringComparison.Ordinal) ? _host.Store.Find(_source.Kind.Kind, name[UserMark.Length..])?.Content
            : _builtIn.FirstOrDefault(p => p.Name == name).Content;
        if (content is null || content.Matches(_source.Read()))
        {
            ShowActual();
            return;
        }
        _source.Write(content.Copy());
        Track(_source.Read());
        _save();
        ShowActual();
    }

    private async Task ManageAsync()
    {
        ShowActual(); // the box never stays on the "Manage presets" row
        await _host.ManagePresetsAsync(new PresetManagerRequest(_source.Kind, _source.Read));
        Refresh();
    }

    /// <summary>Shows the name the settings really have, after the pick that is being handled has finished.</summary>
    private void ShowActual() => Dispatcher.UIThread.Post(Refresh);
}
