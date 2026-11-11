using Avalonia.Threading;
using OmniControl.App.Services;

namespace OmniControl.App.Osd;

/// <summary>Shows the OSD for features that change a value. Features call <see cref="Show"/>; settings decide what appears.</summary>
public sealed class OsdService : IDisposable
{
    /// <summary>Everything that can appear in the OSD. Add a name here when a feature starts calling <see cref="Show"/> with it.</summary>
    public static readonly IReadOnlyList<string> Elements = new[] { "Brightness", "Contrast", "Volume" };

    private readonly DispatcherTimer _hide = new() { Interval = Timing.OsdVisible };
    private readonly WindowsOsd _windowsOsd = new();
    private OsdWindow? _window;

    public OsdSettings Settings { get; } = OsdSettings.Load();

    private readonly BrightnessWatcher _brightness = new();

    public OsdService()
    {
        _hide.Tick += (_, _) => { _hide.Stop(); _window?.Hide(); };
        _windowsOsd.Hidden = Settings.HideWindowsOsd;
        if (!Settings.HideWindowsOsd) _windowsOsd.Restore(); // undo a hide left over from an earlier run

        // Brightness changed outside OmniControl (the laptop's brightness keys, Windows settings): show it here too.
        _brightness.Changed += value =>
        {
            _windowsOsd.Trigger();
            Dispatcher.UIThread.Post(() =>
            {
                if (!RecentOwnChange()) Show("Brightness", value);
            });
        };
        AccentService.Changed += () => SettingsChanged?.Invoke();
        _brightness.Start();

        // Volume changed outside OmniControl (volume keys, another app).
        _volume.Changed += (value, muted) => Dispatcher.UIThread.Post(() =>
        {
            if (!Settings.Enabled || !IsShown("Volume")) return;
            Display(muted ? "Volume (muted)" : "Volume", muted ? 0 : value);
        });
        _volume.Start();
    }

    private readonly VolumeWatcher _volume = new();

    private DateTime _ownChange;

    /// <summary>Call when OmniControl itself changes a value (its sliders), so the echo from Windows doesn't pop up the OSD.</summary>
    public void NoteOwnChange() => _ownChange = DateTime.UtcNow;

    private bool RecentOwnChange() => DateTime.UtcNow - _ownChange < Timing.OsdOwnChangeGrace;

    public bool IsShown(string element) => !Settings.Excluded.Contains(element);

    public void SetShown(string element, bool shown)
    {
        Settings.Excluded.Remove(element);
        if (!shown) Settings.Excluded.Add(element);
        Settings.Save();
    }

    public void SetEnabled(bool on) { Settings.Enabled = on; Settings.Save(); }
    public void SetOpacity(int percent)
    {
        Settings.Opacity = Math.Clamp(percent, 10, 100);
        Settings.Save();
        Preview();
    }

    /// <summary>Raised when an OSD setting changes, so open previews can redraw.</summary>
    public event Action? SettingsChanged;

    public void SetPosition(OsdPosition p) { Settings.Position = p; Settings.Save(); }

    public void SetStyle(OsdStyle s) { Settings.Style = s; Changed(); }
    public void SetAccent(string name) { Settings.Accent = name; Changed(); }
    public void SetBackground(string hex) { Settings.Background = hex; Changed(); }

    private void Changed()
    {
        Settings.Save();
        SettingsChanged?.Invoke();
        Preview();
    }

    public void SetHideWindowsOsd(bool on)
    {
        Settings.HideWindowsOsd = on;
        _windowsOsd.Hidden = on;
        Settings.Save();
    }

    /// <summary>Shows <paramref name="element"/> at <paramref name="value"/> (0-100) unless the OSD or that element is switched off.</summary>
    public void Show(string element, int value)
    {
        if (!Settings.Enabled || !IsShown(element)) return;
        Display(element, value);
    }

    /// <summary>Shows a sample so a changed position can be judged; ignores the element switches.</summary>
    public void Preview() => Display("Preview", 50);

    private void Display(string label, int value)
    {
        _window ??= new OsdWindow();
        _window.Render(Settings, label, value);
        _window.ShowAt(Settings.Position);
        _hide.Stop();
        _hide.Start();
    }

    public void Dispose()
    {
        _hide.Stop();
        _brightness.Dispose();
        _volume.Dispose();
        _windowsOsd.Dispose();
        _window?.Close();
    }
}
