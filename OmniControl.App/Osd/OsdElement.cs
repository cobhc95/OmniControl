namespace OmniControl.App.Osd;

/// <summary>The picture a level pop-up carries. The speaker also turns red when the level is zero (muted).</summary>
public enum OsdLevelIcon { Sun, Speaker, Disc, Thermometer }

/// <summary>One thing the OSD can show (it has its own "Show in OSD" switch): a level pop-up with its icon, or a text pop-up when <see cref="IsLevel"/> is false.</summary>
public sealed record OsdElement(string Name, bool IsLevel, OsdLevelIcon Icon = OsdLevelIcon.Sun);
