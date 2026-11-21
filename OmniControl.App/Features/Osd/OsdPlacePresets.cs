using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Osd;

/// The user's named OSD places as a preset group of the preset manager (the same provider pattern as the layers and the Displays presets):
/// applying a place makes it the one the pop-up uses; the one in use is marked. The seven built-in places are not entries (they always exist).
/// Everything goes through <see cref="OsdService"/>, which keeps the settings.
public sealed class OsdPlacePresets : PresetProvider, IDisposable
{
    /// The preset manager's id of this group (also part of its shortcut actions' ids: never rename).
    public const string GroupId = "osd.places";

    private readonly OsdService _osd;

    public OsdPlacePresets(OsdService osd)
    {
        _osd = osd;
        _osd.SettingsChanged += OnSettingsChanged;
    }

    public override PresetWords Words { get; } = new("Use", "Use OSD place", "+ Save the current position as a place", "Keeps where the OSD is now under a name");

    public override bool CanDuplicate => true;

    public override IReadOnlyList<PresetEntry> Entries => _osd.Places.All.Select(EntryOf).ToList();

    public override PresetEntry? Find(string id) => _osd.Places.Find(id) is { } place ? EntryOf(place) : null;

    private PresetEntry EntryOf(OsdPlace place) => new(place.Id, place.Name)
    {
        InUse = _osd.Settings.CustomPlace == place.Id,
        Tags = new[] { new TagItem($"{OsdSettings.Describe(place.Anchor)}: {place.Across}, {place.Away} px") },
    };

    public override string? Apply(string id)
    {
        _osd.UsePlace(id);
        return null;
    }

    public override PresetChange Add()
    {
        var name = PresetRules.FirstFree("My place", _osd.Places.Names);
        return PresetChange.Made(_osd.Places.Save(name, _osd.Settings.CurrentSpot()).Id);
    }

    public override PresetChange Rename(string id, string name) =>
        _osd.Places.Rename(id, name) is { } problem ? PresetChange.Refused(problem) : PresetChange.Done;

    public override PresetChange Move(string id, int index)
    {
        _osd.Places.Move(id, index);
        return PresetChange.Done;
    }

    public override Action? Delete(string id) => _osd.Places.Delete(id);

    public override PresetChange Duplicate(string id) => _osd.Places.Duplicate(id) is { Length: > 0 } copy ? PresetChange.Made(copy) : PresetChange.Done;

    public override event Action? Changed;

    private void OnSettingsChanged() => Changed?.Invoke();

    public void Dispose() => _osd.SettingsChanged -= OnSettingsChanged;
}
