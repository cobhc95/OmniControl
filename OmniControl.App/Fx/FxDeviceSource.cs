using OmniControl.Core.Engine;
using OmniControl.App.Audio;
using OmniControl.App.Audio.Engine;
using OmniControl.App.Services;

namespace OmniControl.App.Fx;

/// <param name="Tags">What can run on it (Equalizer APO installed for it, a cable can carry it, or neither: "not supported").</param>
public sealed record FolderDevice(string Id, string Name, bool Playing, IReadOnlyList<TagItem> Tags);

/// <summary>
/// The playback devices a device folder can be for, read live from the same places the Volume page's Wiring and Method pieces read
/// (<see cref="MixerService.Devices"/>, <see cref="EffectsSupportService"/>, the rules in <see cref="EffectsRules"/>), and which one plays
/// now (<see cref="MixerService.MasterDeviceId"/>). Never keeps a list: every call asks again. <see cref="Changed"/> says a device came
/// or went, what is installed changed, or the engine's status moved.
/// </summary>
public sealed class FxDeviceSource : IDisposable
{
    public const string ApoTag = "Equalizer APO", CableTag = "via cable", CableMenuTag = "works via cable", NotSupportedTag = "not supported", NotPlayingTag = "not playing";

    private readonly MixerService _mixer;
    private readonly EffectsSupportService _support;
    private readonly EffectsEngine _engine;

    public FxDeviceSource(MixerService mixer, EffectsSupportService support, EffectsEngine engine)
    {
        _mixer = mixer;
        _support = support;
        _engine = engine;
        mixer.Updated += OnChanged;
        support.Changed += OnChanged;
        engine.StatusChanged += OnStatus;
    }

    public event Action? Changed;

    private void OnChanged() => Changed?.Invoke();

    private void OnStatus(string? key)
    {
        if (key is null) Changed?.Invoke();
    }

    /// <summary>The device playing now (the master's real device, else Windows' default); null when none is plugged in.</summary>
    public string? PlayingId => _mixer.MasterDeviceId ?? _mixer.Devices.FirstOrDefault(d => d.IsDefault)?.Id;

    public IReadOnlyList<FolderDevice> Devices() => _mixer.Devices.Where(d => !VirtualCables.IsCable(d.Name)).Select(d => Make(d.Id, d.Name)).ToList();

    /// <summary>The device with this id, or one made from the name the folder remembered when it is not plugged in.</summary>
    public FolderDevice Find(string id, string rememberedName) =>
        Devices().FirstOrDefault(d => d.Id == id) ?? new FolderDevice(id, rememberedName, false, new[] { new TagItem("unplugged") });

    private FolderDevice Make(string id, string name)
    {
        var tags = new List<TagItem>();
        var apo = _support.Current?.Apo ?? Core.Engine.Apo.ApoState.None;
        if (EffectsRules.ApoMissingOn(apo, apo.PlaybackOf(id)) is null) tags.Add(new TagItem(ApoTag, TagTone.Good));
        if (_support.Current is { } support && EffectsRules.NoCableKind(support) == StatusKind.Other) tags.Add(new TagItem(CableMenuTag, TagTone.Cable));
        if (tags.Count == 0) tags.Add(new TagItem(NotSupportedTag));
        return new FolderDevice(id, name, id == PlayingId, tags);
    }

    /// <summary>
    /// The folder row's tag: the device's method as it runs now (read only, the Mixer owns the method) while it plays; "not supported"
    /// when nothing can run on it; "not playing" otherwise (which method it would use is decided when it plays).
    /// </summary>
    public TagItem RowTag(FolderDevice device)
    {
        if (device.Tags.Count == 1 && device.Tags[0].Text == NotSupportedTag) return device.Tags[0];
        if (!device.Playing) return new TagItem(NotPlayingTag);
        return _engine.MethodOf(null) switch
        {
            RunMethod.Apo => new TagItem(ApoTag, TagTone.Good),
            RunMethod.Bus => new TagItem(CableTag, TagTone.Cable),
            _ => new TagItem("no effects running"),
        };
    }

    /// <summary>The folder editor's Method card: what the playing device runs through now, with its delay; empty for a device that is not playing.</summary>
    public TagItem? MethodTag(FolderDevice device)
    {
        if (!device.Playing) return null;
        var method = _engine.MethodFor(null);
        return _engine.MethodOf(null) switch
        {
            RunMethod.Apo => new TagItem($"{ApoTag} · {method.Delay}", TagTone.Good),
            RunMethod.Bus => new TagItem($"Cable · {method.Delay}", TagTone.Cable),
            _ => new TagItem("Nothing running now"),
        };
    }

    public void Dispose()
    {
        _mixer.Updated -= OnChanged;
        _support.Changed -= OnChanged;
        _engine.StatusChanged -= OnStatus;
    }
}
