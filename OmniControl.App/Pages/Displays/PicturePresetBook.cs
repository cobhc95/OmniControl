using OmniControl.App.Display;

namespace OmniControl.App.Pages.Displays;

/// <summary>The Software group's presets: every display's digital picture and the desktop's colour effect, kept in <see cref="DisplaySettingsStore"/>.</summary>
internal sealed class PicturePresetBook : IPresetBook
{
    private readonly DisplaySettingsStore _store;
    private readonly Func<DisplayPreset> _capture;
    private readonly Action<DisplayPreset> _apply;

    /// <param name="capture">Reads the picture the displays show now.</param>
    /// <param name="apply">Shows a saved picture on the displays.</param>
    public PicturePresetBook(DisplaySettingsStore store, Func<DisplayPreset> capture, Action<DisplayPreset> apply)
    {
        (_store, _capture, _apply) = (store, capture, apply);
    }

    public string Label => "Display presets";

    public PresetBookText Text { get; } = new("+ Save the current picture as a preset",
        "Keeps every digital picture setting of every display, and the desktop's saturation and invert, as a new preset", "Picture");

    public IReadOnlyList<INamedPreset> Presets => _store.Presets;

    public IReadOnlyList<string> Names => _store.Presets.Select(p => p.Name).ToList();

    /// <summary>The first saved preset that shows <paramref name="current"/>, or null.</summary>
    public static string? ShownIn(IReadOnlyList<DisplayPreset> presets, DisplayPreset current) =>
        presets.FirstOrDefault(p => p.Matches(current))?.Name;

    public string? Shown => ShownIn(_store.Presets, _capture());

    public string? UnavailableReason => null;

    public void Apply(string name)
    {
        if (_store.Find(name) is { } preset) _apply(preset);
    }

    public void SaveCurrent(string name)
    {
        var preset = _capture();
        preset.Name = name.Trim();
        _store.Save(preset);
    }

    public string? Rename(string from, string to) => _store.Pictures.Rename(from, to);

    public void Move(string name, int index) => _store.Pictures.Move(name, index);

    public Action? Delete(string name) => _store.Pictures.Delete(name);

    public IReadOnlyList<PresetPart> PartsOf(string name) => Array.Empty<PresetPart>();

    public event Action? Changed
    {
        add => _store.PresetsChanged += value;
        remove => _store.PresetsChanged -= value;
    }
}
