using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace OmniControl.Core.Engine.Apo;

/// OmniControl's own Equalizer APO files: the official, unmodified 1.4.2 x64 audio-processing part (the APO and the libraries it loads),
/// shipped in the app folder's <see cref="BundledFolder"/> and copied, after their pinned SHA-256 matched while held open, into
/// <see cref="InstallFolder"/> under Program Files (audiodg reads it there and only administrators can change it).
internal static class ApoFiles
{
    /// Retyped in OmniControl.App.csproj (the copy into the app folder).
    public const string BundledFolder = "equalizer-apo";

    private static readonly (string File, string Sha256)[] Files =
    {
        (ApoIds.UpstreamDllName, "FB73FC8B94F271464AE4F83918BB3CB3243C4F50A8035C07FA538AD16AD66AA4"),
        ("fftw3f.dll", "6C5DF497751D694E9C617F4C931A7F870B64784CB1A0EF09FCCC155BC86D1232"),
        ("sndfile.dll", "4E3BD2DE8E1485110EAEBEF8E1239471F73D608773831C323BF528E05645655E"),
        ("msvcp140.dll", "A4C2229BDC2A2A630ACDC095B4D86008E5C3E3BC7773174354F3DA4F5BEB9CDE"),
        ("vcruntime140.dll", "02C6AA0E6E624411A9F19B0360A7865AB15908E26024510E5C38A9C08362C35A"),
        ("vcruntime140_1.dll", "7DD9AA02E271C68CA6D5F18D651D23A15D7259715AF43326578F7DDE27F37637"),
    };

    public static string InstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ApoIds.OwnFolder);

    public static string ConfigFolder => Path.Combine(InstallFolder, "config");

    public static string Dll => Path.Combine(InstallFolder, ApoIds.UpstreamDllName);

    /// The app folder ships the files (a build without them offers only Equalizer APO's download page).
    public static bool Bundled => File.Exists(Path.Combine(AppContext.BaseDirectory, BundledFolder, ApoIds.UpstreamDllName));

    /// Copies the files that differ from the shipped ones: a file in use (audiodg holds the DLLs) is renamed first and deleted when it can
    /// be (Equalizer APO's installer does the same). Null when every file is in place, else why not; <paramref name="changed"/> = a file
    /// was copied.
    public static string? Place(out bool changed)
    {
        var source = Path.Combine(AppContext.BaseDirectory, BundledFolder);
        var held = new List<FileStream>();
        var renamed = new List<string>();
        changed = false;
        try
        {
            Directory.CreateDirectory(InstallFolder);
            foreach (var (name, sha256) in Files)
            {
                var file = new FileStream(Path.Combine(source, name), FileMode.Open, FileAccess.Read, FileShare.Read);
                held.Add(file);
                if (Convert.ToHexString(SHA256.HashData(file)) != sha256) return $"{name} is not the shipped file";
                var target = Path.Combine(InstallFolder, name);
                if (File.Exists(target) && HashOf(target) == sha256) continue;
                if (File.Exists(target)) renamed.Add(MoveAside(target));
                file.Position = 0;
                using var copy = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                file.CopyTo(copy);
                changed = true;
            }
            Directory.CreateDirectory(ConfigFolder);
            LetUsersWrite(ConfigFolder);
            var config = Path.Combine(ConfigFolder, ApoIds.ConfigFileName);
            if (!File.Exists(config)) File.WriteAllText(config, string.Empty); // no example effects: OmniControl's own line is added when a chain runs
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex.Message;
        }
        finally
        {
            foreach (var file in held) file.Dispose();
            foreach (var old in renamed) DeleteOrLater(old);
        }
    }

    /// Deletes the install folder (files in use go when Windows next starts).
    public static void Delete()
    {
        if (!Directory.Exists(InstallFolder)) return;
        foreach (var file in Directory.EnumerateFiles(InstallFolder, "*", SearchOption.AllDirectories)) DeleteOrLater(file);
        foreach (var folder in Directory.EnumerateDirectories(InstallFolder, "*", SearchOption.AllDirectories).Reverse().Append(InstallFolder))
            DeleteOrLater(folder);
        if (Path.GetDirectoryName(InstallFolder) is { } parent && Directory.Exists(parent) && !Directory.EnumerateFileSystemEntries(parent).Any())
            DeleteOrLater(parent);
    }

    /// Device Selector's backup of a device's slots, kept beside the files as it keeps it beside its own (8-bit text, as its wofstream
    /// writes); a failure to write it stops the install, as its saveToFile's does.
    public static void SaveBackup(string fileName, string text) => File.WriteAllText(Path.Combine(InstallFolder, fileName), text, new UTF8Encoding(false));

    private static string HashOf(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    private static string MoveAside(string path)
    {
        var aside = path + ".old";
        for (var i = 0; File.Exists(aside); i++) aside = $"{path}.old.{i}";
        File.Move(path, aside);
        return aside;
    }

    /// Users get full access to the config folder (OmniControl writes its chain there without administrator rights), as Equalizer APO's
    /// installer grants it (AccessControl::GrantOnFile "(S-1-5-32-545)" "FullAccess").
    private static void LetUsersWrite(string folder)
    {
        var info = new DirectoryInfo(folder);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(acl);
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool MoveFileEx(string existing, string? replacement, int flags);

    private const int DelayUntilReboot = 4;

    private static void DeleteOrLater(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path);
            else File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MoveFileEx(path, null, DelayUntilReboot); // still loaded by audiodg or not empty yet: Windows removes it at the next start
        }
    }
}
