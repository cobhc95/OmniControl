using OmniControl.App.Audio;

namespace OmniControl.App.Fx.Plugins;

/// <summary>
/// One thing the Add effect window can list: a built-in effect (Path holds its catalog kind) or a plug-in file, whose
/// <paramref name="Format"/> is its type's tag (<see cref="PluginType.Tag"/>, as plugins.json stores it).
/// </summary>
public sealed record PluginEntry(string Name, string Path, string Format, string Vendor = "", string Description = "", string Keywords = "", string Icon = "")
{
    public const string BuiltIn = "Built-in";

    public bool IsBuiltIn => Format == BuiltIn;

    /// <summary>The built-in "Device folder" (not an effect: adding it makes a folder, see <see cref="FxFolders"/>).</summary>
    public bool IsFolder => IsBuiltIn && Path == FxFolders.Kind;

    /// <summary>The plug-in's type; null for a built-in effect (or a format this version does not know). Not stored: the tag is.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public PluginType? Type => FxCatalog.PluginTypeByTag(Format);

    /// <summary>The file name's folder, shown as the maker hint for plug-ins that don't tell us one.</summary>
    public string Detail => IsBuiltIn ? Description : Vendor.Length > 0 ? Vendor : System.IO.Path.GetDirectoryName(Path) ?? "";
}
