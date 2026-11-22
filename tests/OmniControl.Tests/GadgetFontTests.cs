using OmniControl.App.Features.Resources;
using OmniControl.App.Features.Resources.Gadget;
using OmniControl.App.Shared.Ui.Controls;

namespace OmniControl.Tests;

/// The gadget's Font card (<see cref="GadgetFontCard.Rows"/>): a picked row stores the font and the chip follows.
public class GadgetFontTests
{
    [Fact]
    public void PickingAFont_StoresItAndTheChipFollows()
    {
        var look = new GadgetSettings();
        var card = new GadgetFontCard(() => look.Font, SetFont(look));
        Pick(look, "Arial");
        card.Item.Refresh();

        Assert.Equal("Arial", look.Font);
        Assert.Equal("Arial", card.Item.Text);
        Assert.True(Rows(look).Single(r => r.Text == "Arial").ShowsBar);
    }

    [Fact]
    public void PickingTheDefault_ClearsTheFont()
    {
        var look = new GadgetSettings();
        var card = new GadgetFontCard(() => look.Font, SetFont(look));
        Pick(look, "Arial");
        Pick(look, GadgetSettings.DefaultFontName);
        card.Item.Refresh();

        Assert.Equal(string.Empty, look.Font);
        Assert.Equal(GadgetSettings.DefaultFontName, card.Item.Text);
        Assert.True(Rows(look)[0].ShowsBar);
    }

    private static Action<string> SetFont(GadgetSettings look) => f => look.Font = f;

    private static List<MenuRowItem> Rows(GadgetSettings look) => GadgetFontCard.Rows(new[] { string.Empty, "Arial" }, look.Font, SetFont(look)).ToList();

    private static void Pick(GadgetSettings look, string name) => Rows(look).Single(r => r.Text == name).Command.Execute(null);
}
