namespace OmniControl.App.Features.Volume.Pages;

/// The Volume page's own app settings: the view left open, whether the Easy welcome was answered, and the Mixer's "Show closed".
public sealed record VolumePageSettings(ISettingSlot<VolumeView> View, ISettingSlot<bool> EasyWelcomeDone, ISettingSlot<bool> ShowClosed);