using OmniControl.App.Audio;

namespace OmniControl.App.Osd;

/// <summary>
/// The places the user named (<see cref="OsdSettings.Places"/>): save, rename, reorder, copy and delete them. Every change is kept in the
/// settings by <paramref name="changed"/> (which saves and tells the listeners). <see cref="OsdPlacePresets"/> shows them to the preset
/// manager; the page's Position menu lists them.
/// </summary>
public sealed class OsdPlaces
{
    private readonly OsdSettings _settings;
    private readonly Action _changed;

    public OsdPlaces(OsdSettings settings, Action changed) => (_settings, _changed) = (settings, changed);

    public IReadOnlyList<OsdPlace> All => _settings.Places;

    public OsdPlace? Find(string id) => _settings.Places.FirstOrDefault(p => p.Id == id);

    public IReadOnlyList<string> Names => _settings.Places.Select(p => p.Name).ToList();

    /// <summary>Keeps <paramref name="name"/> for <paramref name="spot"/>: a place with that name is replaced, else a new one is added. Returns it.</summary>
    public OsdPlace Save(string name, OsdSpot spot)
    {
        var clean = name.Trim();
        var place = _settings.Places.FirstOrDefault(p => PresetRules.Same(p.Name, clean));
        if (place is null)
        {
            _settings.Places.Add(place = OsdPlace.Of(PresetRules.NewId(), clean, spot));
        }
        else (place.Anchor, place.Across, place.Away) = (spot.Anchor, spot.Across, spot.Away);
        _changed();
        return place;
    }

    /// <summary>Why the name cannot be used (empty, too long, taken by another place); null when it can.</summary>
    public string? NameProblem(string name, string? exceptId = null)
    {
        if (PresetRules.Error(name) is { } problem) return problem;
        return _settings.Places.Any(p => p.Id != exceptId && PresetRules.Same(p.Name, name.Trim())) ? "A place with this name exists already." : null;
    }

    public string? Rename(string id, string name)
    {
        if (Find(id) is not { } place) return null;
        if (NameProblem(name, id) is { } problem) return problem;
        place.Name = name.Trim();
        _changed();
        return null;
    }

    public void Move(string id, int index)
    {
        if (Find(id) is not { } place) return;
        _settings.Places.Remove(place);
        _settings.Places.Insert(Math.Clamp(index, 0, _settings.Places.Count), place);
        _changed();
    }

    /// <summary>The copy's id; it is named "name copy".</summary>
    public string Duplicate(string id)
    {
        if (Find(id) is not { } place) return string.Empty;
        var copy = OsdPlace.Of(PresetRules.NewId(), PresetRules.CopyName(place.Name, Names), place.ToSpot());
        _settings.Places.Insert(_settings.Places.IndexOf(place) + 1, copy);
        _changed();
        return copy.Id;
    }

    /// <summary>Removes the place (the pop-up goes back to the built-in place if it was in use); the result puts it back.</summary>
    public Action? Delete(string id)
    {
        if (Find(id) is not { } place) return null;
        var index = _settings.Places.IndexOf(place);
        var wasInUse = _settings.CustomPlace == id;
        _settings.Places.Remove(place);
        if (wasInUse) _settings.CustomPlace = null;
        _changed();
        return () =>
        {
            _settings.Places.Insert(Math.Min(index, _settings.Places.Count), place);
            if (wasInUse) _settings.CustomPlace = id;
            _changed();
        };
    }
}
