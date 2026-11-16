namespace OmniControl.App.Osd;

/// <summary>The picture a level pop-up carries. The speaker and the microphone also turn red when the level is zero (muted); the microphone then shows its crossed-out picture.</summary>
public enum OsdLevelIcon { Sun, Speaker, Microphone, Disc, Thermometer }

/// <summary>One thing the OSD can show (it has its own "Show in OSD" switch): a level pop-up with its own icon (always declared, never defaulted), or a text pop-up (no icon) when <see cref="IsLevel"/> is false.</summary>
public sealed record OsdElement(string Name, bool IsLevel, OsdLevelIcon? Icon);
