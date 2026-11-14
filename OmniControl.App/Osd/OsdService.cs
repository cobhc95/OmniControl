using Avalonia.Threading;
using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Media;

namespace OmniControl.App.Osd;

/// <summary>
/// Shows the OSD for features that change a value. A feature declares each thing it shows once (<see cref="Declare"/>), which gives it
/// its "Show in OSD" switch, then calls <see cref="Show(string, int)"/>; settings decide what appears.
/// </summary>
public sealed class OsdService : IDisposable
{
    // Names this service needs itself: its watchers re-show brightness and volume changed outside OmniControl, and the pop-up picks
    // its icon by them. The features that own those levels declare them.
    public const string BrightnessElement = "Brightness", VolumeElement = "Volume";
    private const string UsbElement = "USB devices", PlayPauseElement = "Play / pause", TrackElement = "Track and artist";

    private readonly List<OsdElement> _elements = new();

    private readonly DispatcherTimer _hide = new() { Interval = Timing.OsdVisible };
    private readonly DispatcherTimer _keepWarm = new() { Interval = Timing.OsdKeepWarm };
    private readonly WindowsOsd _windowsOsd = new();
    private readonly BrightnessWatcher _brightness = new(Timing.OsdBrightnessFirstPoll, Timing.OsdBrightnessPoll);
    private readonly CoreAudio.MasterWatcher _volume = new();
    private readonly UsbDeviceWatcher _usb = new(Timing.OsdUsbSettle);
    private readonly MediaWatcher _media = new(Timing.OsdTrackQuiet);
    private readonly Dictionary<string, DateTime> _ownChange = new();
    private OsdWindow? _window;

    // The newest level asked for from another thread and not drawn yet. Steps that arrive faster than the UI thread draws replace
    // it, so only the newest is drawn and the dispatcher queue never fills with stale values.
    private readonly object _queuedGate = new();
    private (string Element, int Value, string Label)? _queued;
    private readonly Action _showQueued;

    public OsdSettings Settings { get; } = OsdSettings.Load();

    /// <summary>Raised when an OSD setting changes, so open previews can redraw.</summary>
    public event Action? SettingsChanged;

    /// <summary>Raised when a feature declares a new element, so the "Show in OSD" switches follow.</summary>
    public event Action? ElementsChanged;

    /// <summary>Everything that can appear in the OSD: the level pop-ups, then the text pop-ups, each in the order declared.</summary>
    public IReadOnlyList<OsdElement> Elements => _elements.Where(e => e.IsLevel).Concat(_elements.Where(e => !e.IsLevel)).ToList();

    /// <summary>
    /// Declares something a feature shows in the OSD (once, by the feature that shows it), so it gets a "Show in OSD" switch.
    /// <paramref name="isLevel"/> is false for a text pop-up; a level pop-up carries <paramref name="icon"/>.
    /// </summary>
    public void Declare(string name, bool isLevel = true, OsdLevelIcon icon = OsdLevelIcon.Sun)
    {
        if (_elements.Any(e => e.Name == name)) return;
        _elements.Add(new OsdElement(name, isLevel, icon));
        ElementsChanged?.Invoke();
    }

    private OsdLevelIcon IconOf(string element) => _elements.FirstOrDefault(e => e.Name == element)?.Icon ?? OsdLevelIcon.Sun;

    public OsdService()
    {
        _showQueued = ShowQueued;
        Declare(UsbElement, isLevel: false);
        Declare(PlayPauseElement, isLevel: false);
        Declare(TrackElement, isLevel: false);
        _hide.Tick += (_, _) => { _hide.Stop(); _window?.Fade(); };
        _keepWarm.Tick += (_, _) => _window?.KeepWarm();
        _windowsOsd.Hidden = Settings.HideWindowsOsd;
        if (!Settings.HideWindowsOsd) _windowsOsd.Restore(); // undo a hide left over from an earlier run
        AccentService.Changed += OnAccentChanged;

        // Brightness changed outside OmniControl (the laptop's brightness keys, Windows settings): show it here too.
        _brightness.Changed += value =>
        {
            Dispatcher.UIThread.Post(_windowsOsd.Trigger);
            if (!RecentOwnChange(BrightnessElement)) Show(BrightnessElement, value);
        };
        _brightness.Start();

        // Volume changed outside OmniControl (volume keys, another app). OmniControl's own steps already showed their value; the
        // echo would show it again, or briefly show an older value between two quick steps.
        _volume.Changed += (value, muted) =>
        {
            if (!RecentOwnChange(VolumeElement)) ShowVolume(value, muted);
        };
        _volume.Start();

        _usb.Changed += (connected, name) => Dispatcher.UIThread.Post(() => ShowMessage(UsbElement,
            new OsdMessage(connected ? OsdIcon.UsbConnected : OsdIcon.UsbRemoved, connected ? "USB device connected" : "USB device removed", name)));
        _usb.Start();

        _media.Changed += change => Dispatcher.UIThread.Post(() => OnMedia(change));
        _media.Start();

        // As soon as the dispatcher runs, ahead of building the main window's pages, so even the first pop-up is ready.
        Dispatcher.UIThread.Post(() => _ = Window, DispatcherPriority.Send);
        _keepWarm.Start();
    }

    private void OnAccentChanged() => SettingsChanged?.Invoke();

    /// <summary>
    /// The volume pop-up for changes made outside OmniControl watches the device <paramref name="device"/> names (null = Windows' default
    /// playback device); <see cref="RetargetVolume"/> looks again when the answer changes.
    /// </summary>
    public void WatchVolumeOf(Func<string?> device)
    {
        _volume.Device = device;
        RetargetVolume();
    }

    public void RetargetVolume() => _volume.Retarget();

    private void OnMedia(MediaChange change)
    {
        var details = string.Join(" - ", new[] { change.Title, change.Artist }.Where(t => t.Length > 0));
        if (change.IsTrack) ShowMessage(TrackElement, new OsdMessage(OsdIcon.Track, change.Title, change.Artist));
        else ShowMessage(PlayPauseElement, new OsdMessage(change.IsPlaying ? OsdIcon.Play : OsdIcon.Pause, change.IsPlaying ? "Playing" : "Paused", details));
    }

    /// <summary>
    /// Call (from any thread) when OmniControl itself changes <paramref name="element"/> (a slider, a hotkey step), so the echo
    /// Windows reports a moment later doesn't pop up the OSD again or show an older value.
    /// </summary>
    public void NoteOwnChange(string element)
    {
        lock (_ownChange) _ownChange[element] = DateTime.UtcNow;
    }

    private bool RecentOwnChange(string element)
    {
        lock (_ownChange) return _ownChange.TryGetValue(element, out var at) && Grace.Within(at, Timing.OsdOwnChangeGrace);
    }

    /// <summary>Shows the master volume (marked as muted when it is), unless the OSD or the volume element is switched off.</summary>
    public void ShowVolume(int value, bool muted) => Show(VolumeElement, muted ? 0 : value, muted ? "Volume (muted)" : VolumeElement);

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
        Settings.Opacity = OsdSettings.ClampOpacity(percent);
        Settings.Save();
        Preview();
    }

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

    /// <summary>Shows <paramref name="element"/> at <paramref name="value"/> (0-100) unless the OSD or that element is switched off. Any thread.</summary>
    public void Show(string element, int value) => Show(element, value, element);

    /// <summary>As <see cref="Show(string, int)"/>, with a label other than the element's name ("Volume (muted)", "Microphone on").</summary>
    public void Show(string element, int value, string label)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (Settings.Enabled && IsShown(element)) Display(label, IconOf(element), value);
            return;
        }
        // Callers on other threads (slow display writes, hotkey steps) must not wait for the UI thread.
        bool post;
        lock (_queuedGate)
        {
            post = _queued is null;
            _queued = (element, value, label);
        }
        if (post) Dispatcher.UIThread.Post(_showQueued, DispatcherPriority.Send);
    }

    private void ShowQueued()
    {
        (string Element, int Value, string Label)? queued;
        lock (_queuedGate) (queued, _queued) = (_queued, null);
        if (queued is { } q) Show(q.Element, q.Value, q.Label);
    }

    /// <summary>
    /// The pop-up window; the first use creates it and draws every declared level pop-up once, parked off screen. It is first used
    /// from a dispatcher post made at start-up, after the pages (which declare the levels) have been built.
    /// </summary>
    private OsdWindow Window
    {
        get
        {
            if (_window is not null) return _window;
            _window = new OsdWindow();
            _window.Warm(Settings, Elements.Where(e => e.IsLevel).ToList());
            return _window;
        }
    }

    /// <summary>Shows a sample so a changed position can be judged; ignores the element switches.</summary>
    public void Preview() => Display("Preview", OsdLevelIcon.Sun, OsdView.SampleValue);

    /// <summary>Shows a text pop-up for <paramref name="element"/> unless the OSD or that element is switched off.</summary>
    private void ShowMessage(string element, OsdMessage message)
    {
        if (!Settings.Enabled || !IsShown(element)) return;
        Window.RenderMessage(Settings, message);
        Present(newVisual: true);
    }

    private void Display(string label, OsdLevelIcon icon, int value) => Present(Window.Render(Settings, label, icon, value));

    private void Present(bool newVisual)
    {
        Window.ShowAt(Settings.Position, newVisual);
        _hide.Stop();
        _hide.Start();
    }

    public void Dispose()
    {
        _hide.Stop();
        _keepWarm.Stop();
        AccentService.Changed -= OnAccentChanged;
        _brightness.Dispose();
        _volume.Dispose();
        _usb.Dispose();
        _media.Dispose();
        _windowsOsd.Dispose();
        _window?.Close();
    }
}
