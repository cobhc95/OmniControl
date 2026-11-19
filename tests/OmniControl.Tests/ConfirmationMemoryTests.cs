using OmniControl.App.Shared.Dialogs;

namespace OmniControl.Tests;

/// "Don't ask again": only a confirmed, ticked, simple confirmation is remembered; Cancel never is; every choice can be reset.
public sealed class ConfirmationMemoryTests
{
    private static ConfirmationMemory Make(out List<ConfirmationData> saves)
    {
        var list = new List<ConfirmationData>();
        saves = list;
        return new ConfirmationMemory(new ConfirmationData(), list.Add);
    }

    [Fact]
    public void Confirming_with_the_box_ticked_is_remembered_by_kind_and_saved()
    {
        var memory = Make(out var saves);
        memory.Settle(Confirm.Delete("script", "Night"), DialogAnswer.Confirm, dontAskAgain: true);
        Assert.True(memory.Skips(Confirm.Delete("script", "Another name")));
        Assert.False(memory.Skips(Confirm.Delete("rule", "Night")));
        Assert.Single(saves);
        Assert.Equal("Delete a script", saves[0].Remembered["delete.script"]);
    }

    [Fact]
    public void Cancel_is_never_remembered_even_when_ticked()
    {
        var memory = Make(out var saves);
        memory.Settle(Confirm.Delete("script", "Night"), DialogAnswer.Cancel, dontAskAgain: true);
        Assert.False(memory.Skips(Confirm.Delete("script", "Night")));
        Assert.Empty(saves);
    }

    [Fact]
    public void Unticked_confirmations_and_non_simple_messages_are_not_remembered()
    {
        var memory = Make(out _);
        memory.Settle(Confirm.Delete("script", null), DialogAnswer.Confirm, dontAskAgain: false);
        Assert.False(memory.Skips(Confirm.Delete("script", null)));

        var choice = new DialogMessage("Import?", "Replace or merge", "Replace", Other: "Merge", RememberKey: "import");
        memory.Settle(choice, DialogAnswer.Confirm, dontAskAgain: true);
        Assert.False(memory.Skips(choice));
        var plain = new DialogMessage("Allow?", "Run it", "Allow");
        memory.Settle(plain, DialogAnswer.Confirm, dontAskAgain: true);
        Assert.Empty(memory.Items);
    }

    [Fact]
    public void Reset_and_reset_all_ask_again_and_save()
    {
        var memory = Make(out var saves);
        memory.Settle(Confirm.Delete("script", null), DialogAnswer.Confirm, true);
        memory.Settle(Confirm.Delete("rule", null), DialogAnswer.Confirm, true);
        memory.Reset("delete.script");
        Assert.False(memory.Skips(Confirm.Delete("script", null)));
        Assert.True(memory.Skips(Confirm.Delete("rule", null)));
        memory.ResetAll();
        Assert.Empty(memory.Items);
        Assert.Empty(saves[^1].Remembered);
    }

    [Fact]
    public void A_remembered_choice_survives_a_restart()
    {
        var memory = Make(out var saves);
        memory.Settle(Confirm.Delete("device", null), DialogAnswer.Confirm, true);
        var again = new ConfirmationMemory(saves[^1], _ => { });
        Assert.True(again.Skips(Confirm.Delete("3 devices", null, "device")));
    }
}
