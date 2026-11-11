namespace OmniControl.App.Fx.Plugins;

/// <summary>One thing the Add effect window can list: a built-in effect (Path holds its catalog kind) or a plug-in file.</summary>
public sealed record PluginEntry(string Name, string Path, string Format, string Vendor = "", string Description = "")
{
    public const string BuiltIn = "Built-in";
    public const string Vst3 = "VST3";

    public bool IsBuiltIn => Format == BuiltIn;

    /// <summary>The file name's folder, shown as the maker hint for plug-ins that don't tell us one.</summary>
    public string Detail => IsBuiltIn ? Description : Vendor.Length > 0 ? Vendor : System.IO.Path.GetDirectoryName(Path) ?? "";
}
