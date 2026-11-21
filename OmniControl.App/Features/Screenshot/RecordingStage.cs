using System.Security.AccessControl;
using System.Security.Principal;

namespace OmniControl.App.Features.Screenshot;

/// Where a recording waits while its action's "Ask where to save" is on: %LOCALAPPDATA%\OmniControl\Recording, a folder only the signed-in
/// user may open, so the video is never readable or replaceable in a shared folder before the person chose Save. Discard deletes it; what a
/// run that ended in between left there is deleted at the next start (<see cref="CleanUp"/>).
internal static class RecordingStage
{
    private static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OmniControl", "Recording");

    /// The folder, made now or made private again (its access list: the user alone, nothing inherited). Throws what a folder change throws.
    public static string Make()
    {
        using var me = WindowsIdentity.GetCurrent();
        var user = me.User ?? throw new UnauthorizedAccessException("Windows did not say who is signed in.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var folder = new DirectoryInfo(Folder);
        if (folder.Exists) folder.SetAccessControl(security);
        else folder.Create(security);
        return folder.FullName;
    }

    /// True when <paramref name="path"/> waits in the stage (it would be deleted at the next start).
    public static bool Holds(string path) => string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase);

    /// Deletes the recordings a run left waiting (it ended before Save or Discard). Off the UI thread.
    public static void CleanUp()
    {
        if (!Directory.Exists(Folder)) return;
        foreach (var file in Directory.EnumerateFiles(Folder))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppDataStore.AppendLog(Guard.LogFile, $"Screen recording: a recording left unsaved could not be deleted ({ex.Message})");
            }
        }
    }
}
