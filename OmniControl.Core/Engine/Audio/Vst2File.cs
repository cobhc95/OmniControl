using Microsoft.Win32;

namespace OmniControl.Core.Engine;

/// <summary>
/// Tells a VST2 plug-in (.dll) OmniControl can load from any other DLL by reading the file header only (machine and export table,
/// <see cref="PeFile"/>): its code is never loaded or run, so the plug-in scan stays safe. Also the VST2 folder the registry names.
/// </summary>
public static class Vst2File
{
    /// <summary>The entry point every VST 2.4 plug-in exports; Equalizer APO looks only for this one.</summary>
    internal const string Entry = "VSTPluginMain";

    /// <summary>The entry point of plug-ins made for VST 2.3 and older (OmniControl accepts it too).</summary>
    internal const string OldEntry = "main";

    private const string RegistryKey = @"SOFTWARE\VST", RegistryValue = "VSTPluginsPath";

    /// <summary>The one sentence saying why the .dll can't run here, or null when it is a 64-bit VST2 plug-in.</summary>
    public static string? Problem(string path)
    {
        if (!File.Exists(path)) return PluginLoader.Missing;
        var machine = PeFile.Machine(path);
        if (machine is null) return null; // unreadable now: loading it says why
        if (!IsVst2(path)) return "This DLL isn't a VST2 plug-in.";
        return machine == PeFile.MachineX64 ? null : machine == PeFile.MachineX86 ? PluginLoader.Not64Bit : "This plug-in isn't built for 64-bit Windows.";
    }

    /// <summary>True when the file exports a VST2 entry point (any machine; <see cref="Problem"/> checks the machine).</summary>
    private static bool IsVst2(string path)
    {
        var exports = PeFile.Exports(path);
        return exports.Contains(Entry) || exports.Contains(OldEntry);
    }

    /// <summary>True when the plug-in exports <see cref="Entry"/>, the only entry point Equalizer APO calls.</summary>
    public static bool HasApoEntry(string path) => PeFile.Exports(path).Contains(Entry);

    /// <summary>The VST2 folder installers record in HKLM\SOFTWARE\VST (VSTPluginsPath), or null when there is none.</summary>
    public static string? RegisteredFolder()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryKey);
            return key?.GetValue(RegistryValue) is string folder && folder.Trim().Length > 0 ? Environment.ExpandEnvironmentVariables(folder.Trim()) : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null; // not readable: the standard folders are still scanned
        }
    }
}
