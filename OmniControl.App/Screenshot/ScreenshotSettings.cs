using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Screenshot;

/// <summary>What Copy puts on the clipboard (stored by number: never reorder).</summary>
public enum CopyKind { Image, ImageAndPath }

/// <summary>What a screenshot without the editor (window, screen, all screens) does with the picture (stored by number: never reorder).</summary>
public enum QuickKind { CopyAndSave, Copy, Save }

/// <summary>How hard the area overlay keeps itself on top (stored by number: never reorder).</summary>
public enum StayOnTop { Normal, Strong }

/// <summary>One set of screenshot settings: shared by every screenshot action, or one action's own when its "same settings" switch is off.</summary>
public sealed class ScreenshotOptions
{
    public const string DefaultPattern = "Screenshot {date} {time}";

    /// <summary>The folder files are saved in; empty = <see cref="DefaultFolder"/>.</summary>
    public string Folder { get; set; } = string.Empty;

    /// <summary>On: saving a screenshot first asks for its name and folder (the Save screenshot dialog); off: it goes straight to <see cref="FolderInUse"/>.</summary>
    public bool AskFolder { get; set; } = true;

    public string Pattern { get; set; } = DefaultPattern;
    public ImageFormat Format { get; set; } = ImageFormats.DefaultFormat;
    public int Quality { get; set; } = ImageFormats.DefaultQuality;
    public CopyKind CopyAs { get; set; } = CopyKind.Image;
    public QuickKind Quick { get; set; } = QuickKind.CopyAndSave;
    public bool OpenFolder { get; set; }
    public bool ShowNote { get; set; } = true;
    public StayOnTop OnTop { get; set; } = StayOnTop.Normal;

    /// <summary>The folder recordings are saved in; empty = <see cref="DefaultVideoFolder"/>.</summary>
    public string VideoFolder { get; set; } = string.Empty;

    /// <summary>On: a finished recording first asks for its name and folder (the Save recording dialog); off: it is kept in <see cref="VideoFolderInUse"/>.</summary>
    public bool AskVideoFolder { get; set; } = true;

    public VideoFormat VideoFormat { get; set; } = VideoFormat.Mp4H264;
    public int FrameRate { get; set; } = VideoMath.DefaultFrameRate;

    /// <summary>On: the graphics card's encoder when Windows has one (the default); off: software only.</summary>
    public bool HardwareEncoder { get; set; } = true;

    public VideoQuality VideoQuality { get; set; } = VideoQuality.Medium;
    public bool SystemSound { get; set; } = true;
    public bool Microphone { get; set; }
    public bool ShowCursor { get; set; } = true;
    public bool HighlightClicks { get; set; }

    /// <summary>Pictures\Screenshots, Windows' own screenshot folder.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    /// <summary>Videos\OmniControl.</summary>
    public static string DefaultVideoFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "OmniControl");

    /// <summary>The folder a file goes to now.</summary>
    public string FolderInUse => Folder.Trim() is { Length: > 0 } folder ? folder : DefaultFolder;

    /// <summary>The folder a recording goes to now.</summary>
    public string VideoFolderInUse => VideoFolder.Trim() is { Length: > 0 } folder ? folder : DefaultVideoFolder;

    public ScreenshotOptions Clone() => (ScreenshotOptions)MemberwiseClone();
}

/// <summary>The screenshot settings (screenshots.json): the shared set, and the own set of each action whose "same settings" switch is off.</summary>
public sealed class ScreenshotSettings
{
    private static readonly DataStore Store = DataStores.Screenshots;

    public ScreenshotOptions Shared { get; set; } = new();

    /// <summary>On: Windows' own screenshot keys (Print Screen, Win+Shift+S) are turned off (<see cref="WindowsKeysSwitch"/>). One global setting, not part of any action's set.</summary>
    public bool TurnOffWindowsKeys { get; set; }

    /// <summary>What Windows had before the switch was turned on, so turning it off puts it back; null while the switch is off.</summary>
    public SnippingKeysBackup? WindowsKeysBackup { get; set; }

    /// <summary>When the switch last changed (UTC); Win+Shift+S applies once Explorer has restarted after it.</summary>
    public DateTime? WindowsKeysChangedUtc { get; set; }

    /// <summary>By action id: the actions that do not use the shared set.</summary>
    public Dictionary<string, ScreenshotOptions> Own { get; set; } = new();

    /// <summary>
    /// The way of recording to start with on this PC (a fallback that worked, or the one after a way that hung), with the graphics drivers it
    /// was found on; null = the graphics card first. Decided by the recorder (<see cref="RecordingResult.Route"/>), kept here by
    /// <see cref="ScreenshotService"/>; a driver or adapter change makes the recorder try the graphics card again.
    /// </summary>
    public RecordingRoute? RecordingRoute { get; set; }

    /// <summary>The size each drawing tool was last set to, by tool name (<see cref="ToolSizes"/>).</summary>
    public Dictionary<string, double> ToolSizes { get; set; } = new();

    /// <summary>The settings an action uses now.</summary>
    public ScreenshotOptions For(string actionId) => Own.TryGetValue(actionId, out var own) ? own : Shared;

    /// <summary>True while the action uses the shared settings (its switch is on: the default).</summary>
    public bool UsesShared(string actionId) => !Own.ContainsKey(actionId);

    /// <summary>On: the action takes the shared settings again. Off: it starts its own set from a copy of the shared one.</summary>
    public void SetShared(string actionId, bool shared)
    {
        if (shared) Own.Remove(actionId);
        else if (!Own.ContainsKey(actionId)) Own[actionId] = Shared.Clone();
    }

    public static ScreenshotSettings Load() => AppDataStore.Load<ScreenshotSettings>(Store) ?? new();

    public void Save() => AppDataStore.Save(Store, this);
}
