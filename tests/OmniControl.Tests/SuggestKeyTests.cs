using OmniControl.App.Features.Hotkeys.Picker;

namespace OmniControl.Tests;

/// The Actions and hotkeys window's "Suggest" on a scoped action (the display rotation actions).
public class SuggestKeyTests
{
    private static HotkeyAction Rotate(string id, string key) =>
        new(id, id, "Displays", "", _ => { }, "displays", () => new[] { new HotkeyDevice("m1", "Monitor 1") }, group: "Hardware")
        {
            SuggestedKey = key,
        };

    [Fact]
    public void Suggest_OnAScopedAction_GivesItsKeyAndNeverThrows()
    {
        using var registry = new HotkeyRegistry(new HardwareWatches(), _ => { });
        var landscape = Rotate("t.rotate.landscape", "Ctrl+Alt+Up");
        var portrait = Rotate("t.rotate.portrait", "Ctrl+Alt+Left");
        registry.Register(landscape);
        registry.Register(portrait);
        using var block = new ActionHotkeys(landscape, registry, _ => { }, (_, _) => Task.CompletedTask);

        block.SuggestCommand.Execute(null);

        Assert.Equal(new[] { "Ctrl+Alt+Up" }, landscape.Keys);
        Assert.Single(block.Slots);
        Assert.False(block.CanSuggest);

        // Another action holds the key: the box shows whose it is and nothing throws.
        portrait.SetKeys(new[] { "Ctrl+Alt+Up" });
        Assert.Empty(landscape.Keys);
        using var other = new ActionHotkeys(landscape, registry, _ => { }, (_, _) => Task.CompletedTask);
        other.SuggestCommand.Execute(null);
    }
}
