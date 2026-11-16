namespace OmniControl.App.Pages.Volume.Easy.Flow;

/// <summary>Where one line of a setup checklist stands: done, still to do (its fix button can do it), or cannot be done now (its subtitle says why).</summary>
public enum CheckState { Done, Todo, Blocked }

/// <summary>What a fix button asks before it runs: a title, one sentence that says what changes, and the button's words. Null on a fix = it needs no question.</summary>
public sealed record EasyConfirm(string Title, string Text, string Button);

/// <summary>A fix button: its stable id (<see cref="EasyFixes"/>, what <see cref="IEasyActions"/> carries out), its words, and the question it asks first.</summary>
public sealed record EasyFix(string Id, string Label, EasyConfirm? Confirm = null);

/// <summary>
/// One line of a setup checklist, worked out from the live state each time something changes. <see cref="Optional"/> lines (an
/// alternative way) do not have to be done for the setup to be finished. <see cref="Live"/> is a short live word at the right of the line
/// ("0 installed"); a <see cref="Note"/> is a plain sentence with no state mark (the "What you will need" box uses both).
/// </summary>
public sealed record EasyCheck(string Id, string Title, string Subtitle, CheckState State, EasyFix? Fix = null, bool Optional = false,
    string Live = "", bool Note = false)
{
    public bool IsDone => State == CheckState.Done;
}

/// <summary>The ids of every fix a checklist can offer; the one list <see cref="IEasyActions"/> answers.</summary>
public static class EasyFixes
{
    public const string InstallApo = "install-apo", ChooseDevices = "choose-devices", ApplyMasterSound = "apply-master", UseOwnEngine = "use-own-engine",
        ApplyProgramSounds = "apply-programs", ApplyMicrophone = "apply-microphone",
        InstallCable = "install-cable", MoreCables = "more-cables", FreeCable = "free-cable", CreateMicrophone = "create-microphone",
        CreateSpeakers = "create-speakers", PlayOnBoth = "play-on-both";
}
