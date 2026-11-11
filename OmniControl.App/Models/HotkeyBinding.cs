using System.Text.Json;
using System.Text.Json.Serialization;

namespace OmniControl.App.Models;

[Flags]
public enum HotkeyMods { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }

public enum HotkeyScope { Global, ActiveApp }

public sealed record HotkeyBinding(
    string Id,
    string Action,
    HotkeyMods Modifiers,
    int Key,
    HotkeyScope Scope,
    bool Enabled,
    string Command)
{
    public bool SameKeys(HotkeyBinding o) => Modifiers == o.Modifiers && Key == o.Key;

    public static List<HotkeyBinding> Defaults() => new()
    {
        new("vol-up", "Master volume up", HotkeyMods.Ctrl | HotkeyMods.Alt, 0x26, HotkeyScope.Global, true, "SYS v +5"),
        new("vol-down", "Master volume down", HotkeyMods.Ctrl | HotkeyMods.Alt, 0x28, HotkeyScope.Global, true, "SYS v -5"),
        new("mute-app", "Mute active app", HotkeyMods.Ctrl | HotkeyMods.Alt, 0x4D, HotkeyScope.ActiveApp, true, "SYS m 1"),
        new("switch-out", "Switch output device", HotkeyMods.Ctrl | HotkeyMods.Alt, 0x26, HotkeyScope.Global, true, ""),
    };
}

public static class HotkeyStore
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static string PathFile => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OmniControl", "hotkeys.json");

    public static List<HotkeyBinding> Load()
    {
        try
        {
            if (File.Exists(PathFile))
            {
                var list = JsonSerializer.Deserialize<List<HotkeyBinding>>(File.ReadAllText(PathFile), Opts);
                if (list is { Count: > 0 }) return list;
            }
        }
        catch { }
        return HotkeyBinding.Defaults();
    }

    public static bool Save(IEnumerable<HotkeyBinding> list)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile)!);
            File.WriteAllText(PathFile, JsonSerializer.Serialize(list, Opts));
            return true;
        }
        catch { return false; }
    }
}
