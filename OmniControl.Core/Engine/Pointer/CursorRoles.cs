namespace OmniControl.Core.Engine.Pointer;

/// One pointer Windows shows for a purpose: its value name under HKCU\Control Panel\Cursors and the name Windows' Mouse settings give it.
public sealed record CursorRole(string RegistryName, string Name);

/// The pointers of a cursor scheme, in the fixed order a scheme lists their files (a scheme's value under ...\Cursors\Schemes is these
/// files separated by commas, an empty entry meaning Windows' built-in pointer).
public static class CursorRoles
{
    public static readonly IReadOnlyList<CursorRole> All = new CursorRole[]
    {
        new("Arrow", "Normal select"),
        new("Help", "Help select"),
        new("AppStarting", "Working in background"),
        new("Wait", "Busy"),
        new("Crosshair", "Precision select"),
        new("IBeam", "Text select"),
        new("NWPen", "Handwriting"),
        new("No", "Unavailable"),
        new("SizeNS", "Vertical resize"),
        new("SizeWE", "Horizontal resize"),
        new("SizeNWSE", "Diagonal resize 1"),
        new("SizeNESW", "Diagonal resize 2"),
        new("SizeAll", "Move"),
        new("UpArrow", "Alternate select"),
        new("Hand", "Link select"),
        new("Pin", "Location select"),
        new("Person", "Person select"),
    };
}
