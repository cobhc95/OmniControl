using Avalonia.Platform;
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
    public const string BrightnessElement = "Brightness", VolumeElement = "Volume", MicrophoneElement = "Microphone";
    private const string UsbElement = "USB devices", PlayPauseElement = "Play / pause", TrackElement = "Track and artist";

    /// <summary>The smart devices' state changes and failures (shown by <see cref="ShowDeviceNote"/>).</summary>
    public const string SmartDevicesElement = "Smart devices";

    private readonly List<OsdElement> _elements = new();

    private readonly DispatcherTimer _hide = new();
    // Which program caused a pop-up, where that is known (the duration's per-app exceptions).
    private readonly ProgramIdentity _programs = new();
    private readonly DispatcherTimer _keepWarm = new() { Interval = Timing.OsdKeepWarm };
    private readonly WindowsOsd _windowsOsd = new();
    private readonly BrightnessWatcher _brightness = new(Timing.OsdBrightnessFirstPoll, Timing.OsdBrightnessPoll);
    private readonly CoreAudio.MasterWatcher _volume = new();
    private readonly CoreAudio.MasterWatcher _mic = new(recording: true);
    private readonly UsbDeviceWatcher _usb = new(Timing.OsdUsbSettle);
    private readonly MediaWatcher _media = new(Timing.OsdTrackQuiet);
    private readonly Dictionary<string, DateTime> _ownChange = new();
    // One pop-up window per screen it shows on (the first is made at start-up and kept; the others come and go with the "Show on" choice).
    private readonly List<OsdWindow> _windows = new();

    // The newest level asked for from another thread and not drawn yet. Steps that arrive faster than the UI thread draws replace
    // it, so only the newest is drawn and the dispatcher queue never fills with stale values.
    private readonly object _queuedGate = new();
    private (string Element, int Value, string Label, string? Program)? _queued;
    private readonly Action _showQueued;

    public OsdSettings Settings { get; } = OsdSettings.Load();

    /// <summary>The places the user named (the OSD place presets).</summary>
    public OsdPlaces Places { get; }

    /// <summary>How long the pop-up stays, and its exceptions per app or event.</summary>
    public OsdDurations Durations { get; }

    /// <summary>Raised when an OSD setting changes, so open previews can redraw.</summary>
    public event Action? SettingsChanged;

    /// <summary>
    /// Windows reported a new level or mute of the watched playback or recording device (level, muted), from anywhere, as it happens and on a
    /// Windows audio thread; raised before the OSD decides whether to show it. The same notification the pop-up uses, so the states built on
    /// it (the tray's mute tiles) are as fast as the pop-up.
    /// </summary>
    public event Action<int, bool>? VolumeWatched, MicrophoneWatched;

    /// <summary>Raised when a feature declares a new element, so the "Show in OSD" switches follow.</summary>
    public event Action? ElementsChanged;

    /// <summary>Everything that can appear in the OSD: the level pop-ups, then the text pop-ups, each in the order declared.</summary>
    public IReadOnlyList<OsdElement> Elements => _elements.Where(e => e.IsLevel).Concat(_elements.Where(e => !e.IsLevel)).ToList();

    /// <summary>
    /// Declares something a feature shows in the OSD (once, by the feature that shows it), so it gets a "Show in OSD" switch.
    /// A level pop-up always names its own <paramref name="icon"/> (there is no default, so none silently shows another's).
    /// </summary>
    public void Declare(string name, OsdLevelIcon icon) => Add(new OsdElement(name, true, icon));

    /// <summary>Declares a text pop-up (no level, no icon).</summary>
    public void DeclareText(string name) => Add(new OsdElement(name, false, null));

    private void Add(OsdElement element)
    {
        if (_elements.Any(e => e.Name == element.Name)) return;
        _elements.Add(element);
        ElementsChanged?.Invoke();
    }

    // A level shown under a name nobody declared gets the neutral disc, never another feature's picture.
    private OsdLevelIcon IconOf(string element) => _elements.FirstOrDefault(e => e.Name == element)?.Icon ?? OsdLevelIcon.Disc;

    public OsdService()
    {
        _showQueued = ShowQueued;
        Places = new OsdPlaces(Settings, SaveQuietly);
        Durations = new OsdDurations(Settings, SaveQuietly);
        DeclareText(UsbElement);
        DeclareText(PlayPauseElement);
        DeclareText(TrackElement);
        DeclareText(SmartDevicesElement);
        _hide.Tick += (_, _) => { _hide.Stop(); foreach (var window in _windows) window.Fade(); };
        _keepWarm.Tick += (_, _) => { foreach (var window in _windows) window.KeepWarm(); };
        AccentService.Changed += OnAccentChanged;
        if (StartupMode.Inert) return; // a screenshot export hides no Windows pop-up and watches no brightness, volume, USB or media change
        _windowsOsd.Hidden = Settings.HideWindowsOsd;
        if (!Settings.HideWindowsOsd) _windowsOsd.Restore(); // undo a hide left over from an earlier run

        // Brightness changed outside OmniControl (the laptop's brightness keys, Windows settings): show it here too.
        // A change made outside OmniControl may open Windows' own pop-up; only a pop-up opening then is taken for it (WindowsOsd).
        _brightness.Changed += value =>
        {
            if (RecentOwnChange(BrightnessElement)) return;
            _windowsOsd.Trigger();
            Show(BrightnessElement, value);
        };
        _brightness.Start();

        // Volume changed outside OmniControl (volume keys, another app). OmniControl's own steps already showed their value; the
        // echo would show it again, or briefly show an older value between two quick steps. The keys act for the program in front,
        // so that is taken as the program that caused it.
        _volume.Changed += (value, muted) =>
        {
            VolumeWatched?.Invoke(value, muted);
            if (RecentOwnChange(VolumeElement)) return;
            _windowsOsd.Trigger();
            Show(VolumeElement, muted ? 0 : value, VolumeLabel(muted), _programs.Foreground());
        };
        _volume.Start();

        // The microphone changed outside OmniControl (the laptop's mic-mute key, Windows settings, another app).
        _mic.Changed += (value, muted) =>
        {
            MicrophoneWatched?.Invoke(value, muted);
            if (RecentOwnChange(MicrophoneElement)) return;
            _windowsOsd.Trigger();
            Show(MicrophoneElement, muted ? 0 : value, muted ? "Microphone muted" : MicrophoneElement, _programs.Foreground());
        };
        _mic.Start();

        _usb.Failed += ex => Guard.Log("Watching USB devices", ex);
        _usb.Changed += (connected, name) => Dispatcher.UIThread.Post(() => ShowMessage(UsbElement,
            new OsdMessage(connected ? OsdIcon.UsbConnected : OsdIcon.UsbRemoved, connected ? "USB device connected" : "USB device removed", name)));
        _usb.Start();

        _media.Changed += change =>
        {
            _windowsOsd.Trigger(); // a media key opens Windows' media pop-up
            Dispatcher.UIThread.Post(() => OnMedia(change));
        };
        _media.Start();

        // As soon as the dispatcher runs, ahead of building the main window's pages, so even the first pop-up is ready.
        Dispatcher.UIThread.Post(Prepare, DispatcherPriority.Send);
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
        if (change.IsTrack) ShowMessage(TrackElement, new OsdMessage(OsdIcon.Track, change.Title, change.Artist), change.Program);
        else ShowMessage(PlayPauseElement, new OsdMessage(change.IsPlaying ? OsdIcon.Play : OsdIcon.Pause, change.IsPlaying ? "Playing" : "Paused", details), change.Program);
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
    public void ShowVolume(int value, bool muted) => Show(VolumeElement, muted ? 0 : value, VolumeLabel(muted));

    private static string VolumeLabel(bool muted) => muted ? "Volume (muted)" : VolumeElement;

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

    /// <summary>Uses a built-in place.</summary>
    public void SetPosition(OsdPosition p)
    {
        (Settings.Position, Settings.CustomPlace) = (p, null);
        Settings.Save();
        SettingsChanged?.Invoke();
    }

    /// <summary>Uses one of the user's named places (nothing happens for an id that is gone).</summary>
    public void UsePlace(string id)
    {
        if (Places.Find(id) is null) return;
        Settings.CustomPlace = id;
        Settings.Save();
        SettingsChanged?.Invoke();
    }

    /// <summary>Chooses where the pop-up shows; <paramref name="screens"/> (when given) replaces the screens kept for <see cref="OsdShowOn.Chosen"/>.</summary>
    public void SetShowOn(OsdShowOn on, IEnumerable<string>? screens = null)
    {
        Settings.ShowOn = on;
        if (screens is not null) Settings.Screens = screens.ToList();
        Settings.Save();
        SettingsChanged?.Invoke();
        Prepare();
    }

    /// <summary>Ticks or unticks one screen for "Show on"; the first tick turns "Show on" to the chosen screens, the last untick back to all.</summary>
    public void ToggleScreen(string key)
    {
        if (Settings.ShowOn != OsdShowOn.Chosen) (Settings.ShowOn, Settings.Screens) = (OsdShowOn.Chosen, new List<string> { key });
        else if (!Settings.Screens.Remove(key)) Settings.Screens.Add(key);
        if (Settings.Screens.Count == 0) Settings.ShowOn = OsdShowOn.AllScreens;
        Settings.Save();
        SettingsChanged?.Invoke();
        Prepare();
    }

    /// <summary>Every screen Windows has now, in its order (the choices of "Show on" and of the placement window).</summary>
    public IReadOnlyList<Screen> AllScreens => Primary.Screens.All;

    /// <summary>Makes a placement preview that follows the real pop-up's look; <paramref name="dragged"/> hears every spot the user drags or nudges it to.</summary>
    public OsdPreview MakePreview(Action<OsdSpot> dragged) => new(Settings, dragged);

    /// <summary>Takes osd.json from disk as it is now (an import wrote it): the look, places and screens change at once and the listeners are told.</summary>
    public bool ReloadFromDisk()
    {
        SettingsCopy.Into(Settings, OsdSettings.Load());
        if (!StartupMode.Inert)
        {
            _windowsOsd.Hidden = Settings.HideWindowsOsd;
            if (!Settings.HideWindowsOsd) _windowsOsd.Restore();
        }
        SettingsChanged?.Invoke();
        Reloaded?.Invoke();
        ElementsChanged?.Invoke(); // the elements shown may differ
        Prepare();
        return true;
    }

    /// <summary>The settings were read from disk again (an import): a page's cards that hold their own copy of a value show it again.</summary>
    public event Action? Reloaded;

    public void SetStyle(OsdStyle s) { Settings.Style = s; Changed(); }
    public void SetAccent(string name) { Settings.Accent = name; Changed(); }
    public void SetBackground(string hex) { Settings.Background = hex; Changed(); }

    private void Changed()
    {
        Settings.Save();
        SettingsChanged?.Invoke();
        Preview();
    }

    // Saves and tells the listeners without a sample pop-up (the named places or the durations changed).
    private void SaveQuietly()
    {
        Settings.Save();
        SettingsChanged?.Invoke();
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
    public void Show(string element, int value, string label) => Show(element, value, label, null);

    // As above, for a pop-up whose program (the duration's per-app exceptions) is known; null: unknown.
    private void Show(string element, int value, string label, string? program)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            if (Settings.Enabled && IsShown(element)) Display(label, IconOf(element), value, Durations.For(element, program));
            return;
        }
        // Callers on other threads (slow display writes, hotkey steps) must not wait for the UI thread.
        bool post;
        lock (_queuedGate)
        {
            post = _queued is null;
            _queued = (element, value, label, program);
        }
        if (post) Dispatcher.UIThread.Post(_showQueued, DispatcherPriority.Send);
    }

    private void ShowQueued()
    {
        (string Element, int Value, string Label, string? Program)? queued;
        lock (_queuedGate) (queued, _queued) = (_queued, null);
        if (queued is { } q) Show(q.Element, q.Value, q.Label, q.Program);
    }

    /// <summary>
    /// The first pop-up window; the first use creates it and draws every declared level pop-up once, parked off screen. It is first used
    /// from a dispatcher post made at start-up, after the pages (which declare the levels) have been built.
    /// </summary>
    private OsdWindow Primary
    {
        get
        {
            if (_windows.Count > 0) return _windows[0];
            return Add();
        }
    }

    private OsdWindow Add()
    {
        var window = new OsdWindow();
        window.Warm(Settings, Elements.Where(e => e.IsLevel).ToList());
        _windows.Add(window);
        return window;
    }

    /// <summary>Makes (and warms) a window for each screen the pop-up shows on and closes the ones no longer needed, so the first pop-up on each is as quick as on the first.</summary>
    private void Prepare() => Resize(OsdScreens.Targets(Primary.Screens.All, Settings).Count);

    private void Resize(int count)
    {
        while (_windows.Count < count) Add();
        while (_windows.Count > Math.Max(1, count))
        {
            _windows[^1].Close();
            _windows.RemoveAt(_windows.Count - 1);
        }
    }

    /// <summary>Shows a sample so a changed position can be judged; ignores the element switches.</summary>
    public void Preview() => Display("Preview", OsdLevelIcon.Sun, OsdView.SampleValue, TimeSpan.FromSeconds(Durations.EverythingElse));

    /// <summary>
    /// Shows a text pop-up for <paramref name="element"/> (a text element its feature declared) unless the OSD or that element is switched off.
    /// <paramref name="program"/> is the program that caused it when known (the duration's per-app exceptions). UI thread.
    /// </summary>
    public void ShowMessage(string element, OsdMessage message, string? program = null)
    {
        if (!Settings.Enabled || !IsShown(element)) return;
        Present(window => { window.RenderMessage(Settings, message); return true; }, Durations.For(element, program));
    }

    /// <summary>Shows a smart device's state or failure (<see cref="SmartDevicesElement"/>) from any thread.</summary>
    public void ShowDeviceNote(OsdMessage message) => Dispatcher.UIThread.Post(() => ShowMessage(SmartDevicesElement, message));

    private void Display(string label, OsdLevelIcon icon, int value, TimeSpan stays) => Present(window => window.Render(Settings, label, icon, value), stays);

    /// <summary>Draws the pop-up on every screen it shows on (<paramref name="draw"/> says whether a window got a new visual) and starts the hide timer for <paramref name="stays"/>.</summary>
    private void Present(Func<OsdWindow, bool> draw, TimeSpan stays)
    {
        var screens = Primary.Screens.All;
        var targets = OsdScreens.Targets(screens, Settings);
        Resize(targets.Count);
        var spot = Settings.CurrentSpot();
        for (var i = 0; i < targets.Count; i++) _windows[i].ShowAt(spot, targets[i], draw(_windows[i]));
        _hide.Stop();
        _hide.Interval = stays;
        _hide.Start();
    }

    public void Dispose()
    {
        _hide.Stop();
        _keepWarm.Stop();
        AccentService.Changed -= OnAccentChanged;
        _brightness.Dispose();
        _volume.Dispose();
        _mic.Dispose();
        _usb.Dispose();
        _media.Dispose();
        _windowsOsd.Dispose();
        foreach (var window in _windows) window.Close();
        _windows.Clear();
    }
}
