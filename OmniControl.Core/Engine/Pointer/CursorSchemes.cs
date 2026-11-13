using System.Security;
using Microsoft.Win32;

namespace OmniControl.Core.Engine.Pointer;

/// <summary>Where a cursor scheme comes from, as Windows stores it in "Scheme Source": none (Windows' built-in pointers), the user's own, or Windows'.</summary>
public enum CursorSchemeSource { None = 0, User = 1, System = 2 }

/// <summary>A cursor scheme: its registry name, the name to show, where it comes from, and one file per <see cref="CursorRoles"/> entry ("" = built-in).</summary>
public sealed record CursorScheme(string Key, string Name, CursorSchemeSource Source, IReadOnlyList<string> Files)
{
    /// <summary>As Windows' Mouse settings list it: a system scheme is marked so a user scheme of the same name can't be mistaken for it.</summary>
    public string Label => Source == CursorSchemeSource.System ? $"{Name} (system scheme)" : Name;
}

/// <summary>One registry value as it was: its text and whether it was stored with environment names (%SystemRoot%) to expand.</summary>
public sealed record CursorValue(string Text, bool Expand);

/// <summary>Everything under HKCU\Control Panel\Cursors that OmniControl may change, as it was: the scheme name, its source and every role's file (null = no value).</summary>
public sealed record CursorSnapshot(CursorValue? SchemeName, int? SchemeSource, Dictionary<string, CursorValue?> Roles);

/// <summary>
/// The pointer scheme and the pointer of each role, read and written the way Windows' Mouse control panel does it: the role values,
/// the scheme's name and "Scheme Source" under HKCU\Control Panel\Cursors, then SPI_SETCURSORS, which makes Windows load them,
/// saved and announced to every window. Schemes are Windows' (HKLM) and the user's own (HKCU). Nothing here is cached: every call
/// reads the registry again, so what it returns is what Windows has now.
/// </summary>
public static class CursorSchemes
{
    private const string CursorsKey = @"Control Panel\Cursors";
    private const string UserSchemesKey = @"Control Panel\Cursors\Schemes";
    private const string SystemSchemesKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Control Panel\Cursors\Schemes";
    private const string SchemeSourceName = "Scheme Source";
    private const string DefaultValueName = "";

    /// <summary>Windows 10 and 11's default scheme (shown by Windows as "Windows Default"); its registry name is not translated.</summary>
    private const string WindowsDefaultKey = "Windows Aero";

    private const int NameBufferLength = 260;

    /// <summary>The scheme files Windows' pointers can be: anything else is refused before it reaches the registry.</summary>
    public static readonly IReadOnlyList<string> FilePatterns = new[] { "*.cur", "*.ani" };

    /// <summary>Windows' schemes, then the user's own, each sorted by name.</summary>
    public static IReadOnlyList<CursorScheme> List() =>
        Read(Registry.LocalMachine, SystemSchemesKey, CursorSchemeSource.System)
            .Concat(Read(Registry.CurrentUser, UserSchemesKey, CursorSchemeSource.User)).ToList();

    /// <summary>Windows' default scheme, or null on a Windows that has none (then the default is Windows' built-in pointers).</summary>
    public static CursorScheme? WindowsDefault(IReadOnlyList<CursorScheme> schemes) =>
        schemes.FirstOrDefault(s => s.Source == CursorSchemeSource.System && s.Key.Equals(WindowsDefaultKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The scheme whose files are exactly the pointers in force (the one Windows names first when several match), or null when a
    /// pointer was changed on its own or the scheme is not listed.
    /// </summary>
    public static CursorScheme? Current(IReadOnlyList<CursorScheme> schemes)
    {
        var files = CursorRoles.All.Select(r => FileOf(r)).ToList();
        var matching = schemes.Where(s => s.Files.Select(Expand).SequenceEqual(files, StringComparer.OrdinalIgnoreCase)).ToList();
        var named = ReadText(DefaultValueName)?.Text;
        return matching.FirstOrDefault(s => s.Name == named || s.Key == named) ?? matching.FirstOrDefault();
    }

    /// <summary>The file of one role now in force, environment names expanded ("" = Windows' built-in pointer).</summary>
    public static string FileOf(CursorRole role) => Expand(ReadText(role.RegistryName)?.Text ?? string.Empty);

    /// <summary>Makes <paramref name="scheme"/> the pointers in force. False when the registry or Windows refused.</summary>
    public static bool Apply(CursorScheme scheme) => Write(key =>
    {
        for (var i = 0; i < CursorRoles.All.Count; i++) SetText(key, CursorRoles.All[i].RegistryName, scheme.Files[i]);
        SetText(key, DefaultValueName, scheme.Name);
        key.SetValue(SchemeSourceName, (int)scheme.Source, RegistryValueKind.DWord);
    });

    /// <summary>Windows' built-in pointers for every role, no scheme ("None" in Windows' Mouse settings).</summary>
    public static bool ApplyNone() => Write(key =>
    {
        foreach (var role in CursorRoles.All) SetText(key, role.RegistryName, string.Empty);
        SetText(key, DefaultValueName, string.Empty);
        key.SetValue(SchemeSourceName, (int)CursorSchemeSource.None, RegistryValueKind.DWord);
    });

    /// <summary>Uses <paramref name="file"/> for one role ("" = Windows' built-in pointer); the scheme's name is left as it is, as the control panel does.</summary>
    public static bool SetRole(CursorRole role, string file) => Write(key => SetText(key, role.RegistryName, file));

    /// <summary>Why <paramref name="file"/> can't be a pointer, or null when Windows loads it as one. Loads it once and lets it go.</summary>
    public static string? Problem(string file)
    {
        if (!File.Exists(file)) return "That file no longer exists.";
        var extension = Path.GetExtension(file);
        if (!FilePatterns.Any(p => p.TrimStart('*').Equals(extension, StringComparison.OrdinalIgnoreCase)))
            return "Only .cur and .ani pointer files can be used.";
        var cursor = SpiNative.LoadCursorFromFile(file);
        if (cursor == IntPtr.Zero) return "Windows can't use that file as a pointer.";
        SpiNative.DestroyCursor(cursor);
        return null;
    }

    /// <summary>What is under HKCU\Control Panel\Cursors now, for putting it back later.</summary>
    public static CursorSnapshot Snapshot()
    {
        using var key = Registry.CurrentUser.OpenSubKey(CursorsKey);
        return new CursorSnapshot(
            ReadText(DefaultValueName),
            key?.GetValue(SchemeSourceName) as int?,
            CursorRoles.All.ToDictionary(r => r.RegistryName, r => ReadText(r.RegistryName)));
    }

    /// <summary>Puts every value of <paramref name="snapshot"/> back exactly (a value that did not exist is removed), then reloads the pointers.</summary>
    public static bool Restore(CursorSnapshot snapshot) => Write(key =>
    {
        Put(key, DefaultValueName, snapshot.SchemeName);
        if (snapshot.SchemeSource is { } source) key.SetValue(SchemeSourceName, source, RegistryValueKind.DWord);
        else key.DeleteValue(SchemeSourceName, throwOnMissingValue: false);
        foreach (var (name, value) in snapshot.Roles) Put(key, name, value);
    });

    private static void Put(RegistryKey key, string name, CursorValue? value)
    {
        if (value is null) key.DeleteValue(name, throwOnMissingValue: false);
        else key.SetValue(name, value.Text, value.Expand ? RegistryValueKind.ExpandString : RegistryValueKind.String);
    }

    /// <summary>Runs <paramref name="change"/> on the user's Cursors key, then has Windows load the pointers (saved, announced). False when either failed.</summary>
    private static bool Write(Action<RegistryKey> change)
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(CursorsKey, writable: true)) change(key);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
            // A policy or another program locked the key: nothing is reloaded and the caller says it didn't work.
            return false;
        }
        return SpiNative.Set(SpiNative.SetCursors, 0, IntPtr.Zero, SpiNative.SaveAndBroadcast);
    }

    /// <summary>A file path is stored with its environment names kept (as schemes list them), and marked to be expanded when it has any.</summary>
    private static void SetText(RegistryKey key, string name, string text) =>
        key.SetValue(name, text, text.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String);

    private static CursorValue? ReadText(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(CursorsKey);
        if (key?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string text) return null;
        return new CursorValue(text, key.GetValueKind(name) == RegistryValueKind.ExpandString);
    }

    private static string Expand(string file) => Environment.ExpandEnvironmentVariables(file.Trim());

    private static IEnumerable<CursorScheme> Read(RegistryKey hive, string path, CursorSchemeSource source)
    {
        using var key = hive.OpenSubKey(path);
        if (key is null) return Array.Empty<CursorScheme>();
        var schemes = new List<CursorScheme>();
        foreach (var name in key.GetValueNames().Where(n => n.Length > 0))
        {
            if (key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not string data) continue;
            var parts = data.Split(',');
            var files = Enumerable.Range(0, CursorRoles.All.Count).Select(i => i < parts.Length ? parts[i].Trim() : string.Empty).ToList();
            // Windows' own schemes end with their translated name as a resource ("@%SystemRoot%\system32\main.cpl,-1020").
            var resource = parts.Length > CursorRoles.All.Count ? string.Join(',', parts.Skip(CursorRoles.All.Count)).Trim() : null;
            schemes.Add(new CursorScheme(name, NameOf(resource) ?? name, source, files));
        }
        return schemes.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    /// <summary>The text of a "@file,-id" resource, or null when there is none or Windows can't load it.</summary>
    private static string? NameOf(string? resource)
    {
        if (resource is null || !resource.StartsWith('@')) return null;
        var buffer = new char[NameBufferLength];
        if (SpiNative.SHLoadIndirectString(Expand(resource), buffer, (uint)buffer.Length, IntPtr.Zero) != 0) return null;
        var text = new string(buffer, 0, Array.IndexOf(buffer, '\0') is var end and >= 0 ? end : buffer.Length);
        return text.Length > 0 ? text : null;
    }
}
