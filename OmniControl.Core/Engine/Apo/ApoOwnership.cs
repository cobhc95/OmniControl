namespace OmniControl.Core.Engine.Apo;

/// Whose Equalizer APO is on this PC: none, OmniControl's own copy, or one installed separately (which OmniControl never changes).
public enum ApoOwner { None, Ours, Separate }

/// What is left of OmniControl's copy to take away: nothing, only its marker (another install now owns the files), or files and marker.
public enum ApoCleanup { None, MarkerOnly, FilesAndMarker }

/// What the registry and the disk say about the Equalizer APO installs (read by <see cref="ApoStatus"/>, decided by <see cref="ApoOwnership.Decide"/>).
/// <param name="UninstallEntry">Equalizer APO's own Uninstall entry exists (only its installer writes it).</param>
/// <param name="AppKey">HKLM\SOFTWARE\EqualizerAPO exists (its uninstaller leaves it behind).</param>
/// <param name="SeparateFolder">A folder of a separate install that holds EqualizerAPO.dll (its InstallPath or its usual folder), or null.</param>
/// <param name="RegisteredDll">The DLL Windows loads for Equalizer APO's class (its InprocServer32), or null when it is not registered.</param>
/// <param name="RegisteredDllExists">That DLL is on disk.</param>
/// <param name="OwnFolder">OmniControl's own install folder.</param>
/// <param name="Marker">The folder OmniControl's marker names, or null when there is no marker.</param>
public sealed record ApoInstallFacts(bool UninstallEntry, bool AppKey, string? SeparateFolder, string? RegisteredDll, bool RegisteredDllExists,
    string OwnFolder, string? Marker);

/// The decision about who owns Equalizer APO here.
/// <param name="SeparateRemoved">An Equalizer APO was here and has been uninstalled (its key is left over, nothing loads): the page says so.</param>
public sealed record ApoOwnership(ApoOwner Owner, bool SeparateRemoved, ApoCleanup Cleanup)
{
    public static readonly ApoOwnership Nothing = new(ApoOwner.None, false, ApoCleanup.None);

    /// Ours only when the registered DLL is inside OmniControl's folder AND the marker names that folder; any sign of a separate install
    /// wins (its files, its Uninstall entry, a registered DLL elsewhere), and then only OmniControl's leftovers may be taken away: its files
    /// only when the separate install does not use OmniControl's folder.
    public static ApoOwnership Decide(ApoInstallFacts f)
    {
        var dllOurs = f.RegisteredDll is { } dll && IsInside(dll, f.OwnFolder);
        var markerOurs = f.Marker is { } marker && SamePath(marker, f.OwnFolder);
        var separate = f.UninstallEntry || f.SeparateFolder is not null || f.RegisteredDll is not null && f.RegisteredDllExists && !dllOurs;
        if (separate)
        {
            var cleanup = !markerOurs ? ApoCleanup.None
                : dllOurs || f.SeparateFolder is { } other && SamePath(other, f.OwnFolder) ? ApoCleanup.MarkerOnly
                : ApoCleanup.FilesAndMarker;
            return new ApoOwnership(ApoOwner.Separate, false, cleanup);
        }
        if (markerOurs && dllOurs && f.RegisteredDllExists) return new ApoOwnership(ApoOwner.Ours, false, ApoCleanup.None);
        return new ApoOwnership(ApoOwner.None, f.AppKey && !f.RegisteredDllExists, markerOurs ? ApoCleanup.FilesAndMarker : ApoCleanup.None);
    }

    internal static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string file, string folder) =>
        Path.GetDirectoryName(Path.GetFullPath(file)) is { } dir && SamePath(dir, folder);
}
