using OmniControl.App.Shared.Presets;

namespace OmniControl.App.Features.Resources;

/// The Resources presets as a preset group: the built-in "Default" then the user's, each a copy of the live setup; applying copies it back.
public sealed class ResourcePresets : ListPresetProvider<ResourcesPreset>
{
    public const string GroupId = "resources.presets", DefaultName = "Default";
    private const string DefaultId = "resources.default";

    private readonly ResourcesSettings _settings;

    public ResourcePresets(ResourcesSettings settings) : base(new PresetList<ResourcesPreset>(settings.Presets, settings.Save), builtIns: 1)
    {
        _settings = settings;
        settings.Changed += NotifyChanged; // the live setup may now be (or no longer be) a preset
    }

    public override PresetWords Words { get; } = new("Apply", "Apply Resources preset", "+ Save the current setup as a preset",
        "Keeps the read interval, the readings' choices and the taskbar gadget");

    protected override IReadOnlyList<PresetEntry> MakeEntries() => List.Items.Select(p => new PresetEntry(p.Id, p.Name) { InUse = p.Setup.Same(_settings.Setup) })
        .Prepend(new PresetEntry(DefaultId, DefaultName) { InUse = new ResourcesSetup().Same(_settings.Setup), BuiltIn = true, CanRename = false, CanDelete = false, CanMove = false })
        .ToList();

    public override string? Apply(string id)
    {
        var setup = id == DefaultId ? new ResourcesSetup() : List.Items.FirstOrDefault(p => p.Id == id)?.Setup.Copy();
        if (setup is null) return "That preset is gone.";
        SettingsCopy.Into(_settings.Setup, setup);
        _settings.Commit();
        return null;
    }

    public override PresetChange Add()
    {
        var name = PresetRules.FirstFree("Resources", List.Items.Select(p => p.Name).Append(DefaultName).ToList());
        var preset = new ResourcesPreset { Name = name, Setup = _settings.Setup.Copy() };
        List.Save(preset);
        return PresetChange.Made(preset.Id);
    }
}
