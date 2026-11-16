using OmniControl.App.Display;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Displays;

/// <summary>What a monitor preset needs of one display: what it shows now and a way to show and write the values of an entry.</summary>
internal interface IMonitorPresetTarget
{
    string Id { get; }
    string Name { get; }

    /// <summary>What the display shows now (what OmniControl last read or set); null for a display with nothing a preset keeps.</summary>
    LiveMonitor? Snapshot();

    /// <summary>Shows the values on the display's cards and writes them to the monitor (queued, one write at a time).</summary>
    void Write(IReadOnlyList<PlannedWrite> writes);
}

/// <summary>A display as a monitor preset sees it. Presets keep every level and choice the display reports, whatever answers it (DDC/CI, or Windows' brightness for a laptop's panel). Orientation is not kept: it is not a VCP feature and turning a screen is not part of a picture setting.</summary>
internal sealed partial class HardwareDisplayItem : IMonitorPresetTarget
{
    public LiveMonitor? Snapshot()
    {
        if (_capabilities is null) return null;
        var features = new List<LiveFeature>();
        foreach (var (level, slider) in _sliders)
        {
            if (slider.Availability.IsAvailable && _capabilities.Level(level.Code) is { } range)
                features.Add(new LiveFeature(level.Code, slider.Value, range.Maximum, Array.Empty<uint>()));
        }
        foreach (var (choice, card) in _choices)
        {
            var values = _values.GetValueOrDefault(choice);
            if (!card.Availability.IsAvailable || values?.FirstOrDefault(v => v.Name == card.Selected) is not { } shown) continue;
            features.Add(new LiveFeature(choice.Feature.Code, (int)shown.Value, 0, values.Select(v => v.Value).ToList()));
        }
        return features.Count == 0 ? null : new LiveMonitor(Id, Name, features);
    }

    public void Write(IReadOnlyList<PlannedWrite> writes)
    {
        foreach (var write in writes)
        {
            if (write.Kind == VcpKind.Level) WriteLevel(write);
            else WriteChoice(write);
        }
    }

    /// <summary>The slider takes the value (without writing it itself) and the newest wanted level goes to the monitor through the shared writer.</summary>
    private void WriteLevel(PlannedWrite write)
    {
        var level = _sliders.Keys.FirstOrDefault(l => l.Code == write.Code);
        if (level is null) return;
        _sliders[level].Load(write.Value);
        _levels.Set(KeyOf(level), write.Value, p => Write(level, p));
    }

    private void WriteChoice(PlannedWrite write)
    {
        var choice = _choices.Keys.FirstOrDefault(c => c.Feature.Code == write.Code);
        if (choice is null || _values.GetValueOrDefault(choice)?.FirstOrDefault(v => v.Value == (uint)write.Value) is not { } value) return;
        var card = _choices[choice];
        card.Load(card.Options, value.Name);
        _levels.Set($"{Id}:{write.Code}:choice", write.Value, raw => Fail(_access.WriteRaw(choice.Feature.Code, (uint)raw)));
    }
}
