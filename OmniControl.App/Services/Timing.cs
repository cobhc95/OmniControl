namespace OmniControl.App.Services;

/// <summary>Every interval and grace period the app uses, in one place, so a timing change never means a hunt through the code.</summary>
public static class Timing
{
    /// <summary>How often the mixer re-reads programs and devices from Windows.</summary>
    public static readonly TimeSpan MixerRefresh = TimeSpan.FromSeconds(2);

    /// <summary>After the user changes a program's volume or device, ignore what Windows reports for this long.</summary>
    public static readonly TimeSpan MixerUserChangeGrace = TimeSpan.FromSeconds(3);

    /// <summary>After the user moves a master or input slider, don't overwrite it with the system value for this long.</summary>
    public static readonly TimeSpan SliderEchoGrace = TimeSpan.FromSeconds(2);

    /// <summary>Refresh rate of the Volume page's level meters (25 per second).</summary>
    public static readonly TimeSpan MeterFrame = TimeSpan.FromMilliseconds(40);

    /// <summary>How long the OSD pop-up stays visible.</summary>
    public static readonly TimeSpan OsdVisible = TimeSpan.FromMilliseconds(1500);

    /// <summary>Changes made by OmniControl itself within this window don't pop the OSD a second time.</summary>
    public static readonly TimeSpan OsdOwnChangeGrace = TimeSpan.FromSeconds(1);

    /// <summary>How often Windows' own OSD window is looked for so it can be hidden.</summary>
    public static readonly TimeSpan WindowsOsdPoll = TimeSpan.FromMilliseconds(60);

    /// <summary>Minimum gap between attempts to create the hiding helper for Windows' OSD.</summary>
    public static readonly TimeSpan WindowsOsdRetry = TimeSpan.FromSeconds(2);

    /// <summary>Slide animation when a dragged list item makes room.</summary>
    public static readonly TimeSpan DragSlide = TimeSpan.FromMilliseconds(110);
}
