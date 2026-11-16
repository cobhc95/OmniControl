using Avalonia.Threading;
using OmniControl.App.Audio;
using OmniControl.App.Pages;

namespace OmniControl.App.Fx;

/// <summary>Where the preset pull-down gets the user's presets and opens the preset manager (the effect chain).</summary>
internal interface IPresetHost
{
    UserPresetStore Store { get; }

    /// <summary>Opens the shared preset manager at this kind's card.</summary>
    void ManagePresets(FxKind kind);
}

/// <summary>
/// The effect the preset manager (and a preset's shortcut) reads and applies an effect preset to: an FX window's effect, through its own
/// preset box (<see cref="FxSlotViewModel"/>), or an effect no window shows (<see cref="Presets.SlotPresetTarget"/>).
/// </summary>
internal interface IFxPresetTarget
{
    PresetContent Read();

    void Apply(PresetContent content);
}

/// <summary>How one effect reads, shows and keeps its settings as a preset; the effect's view-model supplies it.</summary>
internal sealed record PresetSource(FxKind Kind, Func<PresetContent> Read, Action<PresetContent> Write, Func<PresetContent?> GetCustom, Action<PresetContent?> SetCustom);

/// <summary>
/// The preset pull-down of any effect with presets (the three equalisers, the compressor), built once for all of them: the built-in
/// presets, the user's own (marked with a star) and "Custom"; the chip that shows it (<c>Controls/PresetPicker</c>) adds "Manage presets…" (the choice's <see cref="ChoiceItem.Manage"/>). The shown name is always worked out from the
/// settings (the preset that matches them, <see cref="PresetCatalog.Matching"/>), so it is right after a restart without storing it.
/// When a preset replaces hand-tuned settings they are kept on the effect (<see cref="FxSlot.Custom"/>) and "Custom" brings them back.
/// The preset manager applies a preset through <see cref="Apply"/>, the same way as a pick.
/// </summary>
internal sealed class PresetPicker : IDisposable
{
    public const string CustomName = "Custom", UserMark = "★ ";

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
        Choice = new ChoiceItem("Preset", OptionList(), NameFor(_last), OnPick) { Manage = () => host.ManagePresets(source.Kind) };
        host.Store.Changed += Refresh;
    }

    public ChoiceItem Choice { get; }

    /// <summary>The effect's settings as they are now.</summary>
    public PresetContent Read() => _source.Read();

    /// <summary>The user changed the settings by hand (or a button such as Flat did): the shown name follows, and hand-tuned settings that a preset replaced are kept.</summary>
    public void Edited()
    {
        Track(_source.Read());
        Refresh();
    }

    /// <summary>Shows a preset's content on the effect (a pick here, or the preset manager), keeping hand-tuned settings it replaces, and saves.</summary>
    public void Apply(PresetContent content)
    {
        if (content.Matches(_source.Read()))
        {
            ShowActual();
            return;
        }
        _source.Write(content.Copy());
        Track(_source.Read());
        _save();
        ShowActual();
    }

    public void Dispose() => _host.Store.Changed -= Refresh;

    private List<string> OptionList() => _builtIn.Select(p => p.Name)
        .Concat(_host.Store.Names(_source.Kind.Kind).Select(n => UserMark + n))
        .Append(CustomName).ToList();

    private string NameFor(PresetContent content) =>
        PresetCatalog.Matching(_source.Kind, content, _host.Store.Of(_source.Kind.Kind)) is { } match
            ? match.BuiltIn ? match.Name : UserMark + match.Name
            : CustomName;

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
        var content = name == CustomName ? _source.GetCustom()
            : name.StartsWith(UserMark, StringComparison.Ordinal) ? _host.Store.Find(_source.Kind.Kind, name[UserMark.Length..])?.Content
            : _builtIn.FirstOrDefault(p => p.Name == name).Content;
        if (content is null) ShowActual();
        else Apply(content);
    }

    /// <summary>Shows the name the settings really have, after the pick that is being handled has finished.</summary>
    private void ShowActual() => Dispatcher.UIThread.Post(Refresh);
}
