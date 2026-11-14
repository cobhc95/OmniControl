using Avalonia.Input;
using OmniControl.App.Pages;
using Xunit;

namespace OmniControl.Tests;

public class ComboKeysTests
{
    [Theory]
    [InlineData(Key.Enter, KeyModifiers.None)]
    [InlineData(Key.Space, KeyModifiers.None)]
    [InlineData(Key.F4, KeyModifiers.None)]
    [InlineData(Key.Down, KeyModifiers.Alt)]
    public void Closed_list_opens(Key key, KeyModifiers mods) =>
        Assert.Equal(ComboKeys.Act.Open, ComboKeys.Decide(key, mods, false, 3, 0).Act);

    [Fact]
    public void Closed_list_leaves_arrows_to_the_card_walk() =>
        Assert.Equal(ComboKeys.Act.None, ComboKeys.Decide(Key.Down, KeyModifiers.None, false, 3, 0).Act);

    [Theory]
    [InlineData(Key.Down, 2, 0)]
    [InlineData(Key.Up, 0, 2)]
    [InlineData(Key.Down, 0, 1)]
    [InlineData(Key.Home, 2, 0)]
    [InlineData(Key.End, 0, 2)]
    public void Open_list_moves_and_wraps(Key key, int current, int expected)
    {
        var r = ComboKeys.Decide(key, KeyModifiers.None, true, 3, current);
        Assert.Equal(ComboKeys.Act.Highlight, r.Act);
        Assert.Equal(expected, r.Index);
    }

    [Fact]
    public void Open_list_picks_the_highlighted_entry()
    {
        var r = ComboKeys.Decide(Key.Enter, KeyModifiers.None, true, 3, 1);
        Assert.Equal(ComboKeys.Act.Pick, r.Act);
        Assert.Equal(1, r.Index);
    }

    [Fact]
    public void Escape_closes_an_open_list() =>
        Assert.Equal(ComboKeys.Act.Close, ComboKeys.Decide(Key.Escape, KeyModifiers.None, true, 3, 1).Act);

    [Fact]
    public void Typing_finds_the_next_entry_by_prefix()
    {
        string[] labels = ["Ask each time", "Minimise to tray", "Exit completely", "Minimal"];
        Assert.Equal(1, ComboKeys.FindByPrefix(labels, "m", 0));
        Assert.Equal(3, ComboKeys.FindByPrefix(labels, "m", 1));
        Assert.Equal(-1, ComboKeys.FindByPrefix(labels, "z", 0));
    }
}
