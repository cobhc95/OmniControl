namespace OmniControl.App.Screenshot;

/// <summary>
/// The size each drawing tool uses now, stepped through its list in <see cref="MarkLook"/> and remembered per tool (in the screenshot
/// settings, so the next overlay starts with it). The store is the settings' table by tool name; <c>save</c> writes the settings.
/// </summary>
public sealed class ToolSizes
{
    private readonly Dictionary<string, double> _store;
    private readonly Action _save;

    public ToolSizes(Dictionary<string, double> store, Action save)
    {
        _store = store;
        _save = save;
    }

    /// <summary>True for a tool that has a size.</summary>
    public static bool Has(DrawTool tool) => MarkLook.SizesOf(tool) is not null;

    /// <summary>The tool's size now (screen units); 0 for a tool without one.</summary>
    public double Of(DrawTool tool)
    {
        if (MarkLook.SizesOf(tool) is not { } look) return 0;
        return _store.TryGetValue(tool.ToString(), out var size) && look.Sizes.Contains(size) ? size : look.Start;
    }

    /// <summary>One step bigger (+1) or smaller (-1), held at the list's ends. True when the size changed.</summary>
    public bool Step(DrawTool tool, int direction)
    {
        if (MarkLook.SizesOf(tool) is not { } look) return false;
        var list = look.Sizes.ToList();
        var at = Math.Clamp(list.IndexOf(Of(tool)) + direction, 0, list.Count - 1);
        if (list[at] == Of(tool)) return false;
        _store[tool.ToString()] = list[at];
        _save();
        return true;
    }
}
