using OmniControl.App.Display;
using OmniControl.App.Pages;
using OmniControl.App.Pages.Displays;

namespace OmniControl.Tests;

/// <summary>The Software group's preset drop-down (the shared <see cref="ProviderChoice"/> over its <see cref="PicturePresetBook"/>): what it lists and reads (pure), and that picking applies through the given function (a fake; no display is touched).</summary>
public class DisplayPresetPickerTests
{
    private static DisplayPreset Preset(string name, int brightness = 100, int saturation = 100, bool invert = false)
    {
        var preset = new DisplayPreset { Name = name, Saturation = saturation, Invert = invert };
        preset.Displays["D1"] = new PictureValues { Brightness = brightness };
        return preset;
    }

    private static DisplaySettingsStore Store(params DisplayPreset[] presets)
    {
        var data = new DisplaySettings();
        data.Presets.AddRange(presets);
        return new DisplaySettingsStore(data, _ => { });
    }

    private static ProviderChoice Picker(IPresetBook book) => new(book.Label, new BookPresets(book), () => { }, withCustom: true);

    [Fact]
    public void NoPresets_LeadEntryIsCustom()
    {
        var current = Preset("");
        using var picker = Picker(new PicturePresetBook(Store(), () => current, _ => { }));
        Assert.Equal(new[] { ProviderChoice.CustomText }, picker.Choice.Options);
        Assert.Null(PicturePresetBook.ShownIn(Array.Empty<DisplayPreset>(), current));
    }

    [Fact]
    public void MatchingPreset_IsShown_AChangedValueReadsCustom()
    {
        var presets = new[] { Preset("Night", 40), Preset("Day", 100) };
        Assert.Equal("Night", PicturePresetBook.ShownIn(presets, Preset("", 40)));
        Assert.Null(PicturePresetBook.ShownIn(presets, Preset("", 55)));
        Assert.Null(PicturePresetBook.ShownIn(presets, Preset("", 40, saturation: 50)));
        Assert.Null(PicturePresetBook.ShownIn(presets, Preset("", 40, invert: true)));
        using var picker = Picker(new PicturePresetBook(Store(presets), () => Preset("", 55), _ => { }));
        Assert.Equal(new[] { ProviderChoice.CustomText, "Night", "Day" }, picker.Choice.Options);
    }

    [Fact]
    public void PickingAPreset_AppliesItAndShowsIt()
    {
        var store = Store(Preset("Night", 40), Preset("Day", 100));
        var shown = Preset("", 100);
        DisplayPreset? applied = null;
        using var picker = Picker(new PicturePresetBook(store, () => shown, p => { applied = p; shown = p; }));
        Assert.Equal("Day", picker.Choice.Selected);

        picker.Choice.Selected = "Night";

        Assert.Equal("Night", applied?.Name);
        Assert.Equal("Night", picker.Choice.Selected);
    }

    [Fact]
    public void PickingTheLeadEntry_AppliesNothing_AndSavedChangesRefreshTheList()
    {
        var store = Store();
        DisplayPreset? applied = null;
        using var picker = Picker(new PicturePresetBook(store, () => Preset("", 70), p => applied = p));
        Assert.Equal(ProviderChoice.CustomText, picker.Choice.Selected);

        var saved = Preset("Mine", 70);
        store.Save(saved);

        Assert.Equal("Mine", picker.Choice.Selected);
        Assert.Equal(new[] { ProviderChoice.CustomText, "Mine" }, picker.Choice.Options);
        picker.Choice.Selected = ProviderChoice.CustomText;
        Assert.Null(applied);
    }
}
