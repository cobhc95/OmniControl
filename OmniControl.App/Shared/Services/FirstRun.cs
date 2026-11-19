namespace OmniControl.App.Shared.Services;

/// The one rule for which Volume view a start opens in when none was recorded yet: a new install (no settings or mixer file existed
/// before this start) opens in Easy with its welcome; anyone who already had settings stays in Advanced and is never welcomed. Pure.
public static class FirstRun
{
    /// Records the decision in <paramref name="settings"/> when it has none; true when it did (the caller saves).
    public static bool Decide(AppSettings settings, bool earlierFilesExisted)
    {
        if (settings.VolumeView != VolumeView.Undecided) return false;
        settings.VolumeView = earlierFilesExisted ? VolumeView.Advanced : VolumeView.Easy;
        settings.EasyWelcomeDone = earlierFilesExisted;
        return true;
    }
}
