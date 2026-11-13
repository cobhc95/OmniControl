using Avalonia.Media;
using OmniControl.Core.Models;
using OmniControl.App.Services;

namespace OmniControl.App.Osd;

/// <summary>OSD preferences, stored as JSON in %APPDATA%\OmniControl\osd.json.</summary>
public sealed class OsdSettings
{
    public bool Enabled { get; set; } = true;
    public OsdPosition Position { get; set; } = OsdPosition.BottomCenter;
    public OsdStyle Style { get; set; } = OsdStyle.Pill;
    /// <summary>"Match program" (follow the program accent) or the name of an AccentService preset.</summary>
    public string Accent { get; set; } = MatchProgram;

    public const string MatchProgram = "Match program";

    /// <summary>The accent colour to draw with: the program accent unless a preset was chosen.</summary>
    public Color ResolveAccent() => ResolveAccent(Accent);

    /// <summary>The colour of an accent choice. Asked on every pop-up step, so it is a plain loop that allocates nothing.</summary>
    public static Color ResolveAccent(string name)
    {
        if (name == MatchProgram) return AccentService.Current;
        for (var i = 0; i < OsdPalette.ExtraAccents.Count; i++)
            if (OsdPalette.ExtraAccents[i].Name == name) return OsdPalette.ExtraAccents[i].Color;
        return AccentService.Resolve(name);
    }

    public string Background { get; set; } = Palette.CharcoalHex;

    /// <summary>How opaque the OSD is, 10-100 percent.</summary>
    public int Opacity { get; set; } = Percent.Max;

    public const int MinOpacity = 10;

    public static int ClampOpacity(int percent) => Math.Clamp(percent, MinOpacity, Percent.Max);

    /// <summary>Elements the user excluded from the OSD (everything else is shown).</summary>
    public List<string> Excluded { get; set; } = new();

    /// <summary>Suppress Windows' own volume/brightness pop-up.</summary>
    public bool HideWindowsOsd { get; set; }

    private const string FileName = "osd.json";

    public static OsdSettings Load() => AppDataStore.Load<OsdSettings>(FileName) ?? new();

    public void Save() => AppDataStore.Save(FileName, this);

    public static string Describe(OsdPosition p) => p switch
    {
        OsdPosition.TopLeft => "Top left",
        OsdPosition.TopCenter => "Top center",
        OsdPosition.TopRight => "Top right",
        OsdPosition.Center => "Center",
        OsdPosition.BottomLeft => "Bottom left",
        OsdPosition.BottomCenter => "Bottom center",
        _ => "Bottom right",
    };

    public static string Describe(OsdStyle s) => s switch
    {
        OsdStyle.Classic => "Classic",
        OsdStyle.Pill => "Compact pill",
        OsdStyle.BigNumber => "Big number",
        OsdStyle.Ring => "Ring",
        _ => "Vertical",
    };
}
