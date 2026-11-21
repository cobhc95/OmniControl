namespace OmniControl.App.Shared.Services;

// The intervals and limits of Smart Devices and their simulation; a part of Timing in Shared/Services, so every call stays Timing.X.
public static partial class Timing
{
    /// How often a step looks whether its device's command has ended, and how long nothing may be running before it counts as "nothing ran".
    public static readonly TimeSpan StepOutcomePoll = TimeSpan.FromMilliseconds(500), StepOutcomeIdle = TimeSpan.FromSeconds(10);

    /// The longest a step waits for its device's command to end (a simulation can take two minutes).
    public static readonly TimeSpan StepOutcomeWait = TimeSpan.FromMinutes(3);

    /// A line of devices-log.txt keeps at most this many characters (a command's whole answer is in the device's own details).
    public const int DeviceLogLineMax = 600;

    /// A smart plug command (kasa) is stopped after this long.
    public static readonly TimeSpan DeviceCommandTimeout = TimeSpan.FromSeconds(30);

    /// The quick looks at what is installed for the Devices page (is python-kasa there) are stopped after this long.
    public static readonly TimeSpan DeviceProbeTimeout = TimeSpan.FromSeconds(20);

    /// Installing python-kasa with pip (a download) is stopped after this long.
    public static readonly TimeSpan DeviceInstallTimeout = TimeSpan.FromMinutes(5);

    /// A simulated device's step waits this long for its element or text to show, looking again every poll; after a tap, typing or swipe it lets the screen settle; a whole run is stopped after the run timeout.
    public static readonly TimeSpan DeviceSimElementWait = TimeSpan.FromSeconds(10), DeviceSimPoll = TimeSpan.FromMilliseconds(700),
        DeviceSimSettle = TimeSpan.FromMilliseconds(600), DeviceSimRunTimeout = TimeSpan.FromMinutes(2);

    /// How often adb is asked which emulators are up, so the set-up checklist follows an emulator opening or closing; and how long a tap on a home screen waits for the app to open.
    public static readonly TimeSpan DeviceSimLive = TimeSpan.FromSeconds(3), DeviceSimLaunchWait = TimeSpan.FromSeconds(1.5);

    /// How often the memory the emulator's programs hold is read again for the Simulation row's chip (a gentle background look; the figure only needs to be roughly right).
    public static readonly TimeSpan EmulatorMemoryRefresh = TimeSpan.FromSeconds(10);

    /// "Keep running: until idle" closes the emulator after this many minutes without an action (the default; the user types another).
    public const int EmulatorIdleMinutes = 30;

    /// A woken emulator is waited for this long to finish starting (a cold boot); a quick boot from a snapshot gets the shorter time before one cold boot is tried; adb is asked every poll.
    public static readonly TimeSpan EmulatorBootTimeout = TimeSpan.FromMinutes(5), EmulatorQuickBootTimeout = TimeSpan.FromSeconds(120), EmulatorBootPoll = TimeSpan.FromSeconds(2),
        EmulatorWindowPoll = TimeSpan.FromMilliseconds(150), EmulatorWindowWait = TimeSpan.FromSeconds(60);

    /// Two reports of a tap closer than this in time and <see cref="DeviceSimSameTouchPixels"/> in place are one tap.
    public static readonly TimeSpan DeviceSimSameTouch = TimeSpan.FromMilliseconds(400);

    public const int DeviceSimSameTouchPixels = 24;

    /// How many times a step that waits for an element scrolls the page to find it, before it gives up.
    public const int DeviceSimScrollTries = 4;

    /// While steps are being recorded the screen is read again this often, so a tap is matched with the screen it was made on.
    public static readonly TimeSpan DeviceSimRecordGap = TimeSpan.FromMilliseconds(900);

    /// Teaching on and off: each "It's on now" / "It's off now" takes this many looks at the device's card, this far apart.
    public const int DeviceSimTeachLooks = 3;

    public static readonly TimeSpan DeviceSimTeachGap = TimeSpan.FromMilliseconds(500);

    /// A device's name typed in its box is saved this long after the last key (the list and its actions follow every key at once).
    public static readonly TimeSpan DeviceNameSaveDelay = TimeSpan.FromMilliseconds(600);

    /// How often a device with "Every 5 min" / "Every 15 min" reads its real state.
    public static readonly TimeSpan DeviceAutoCheck5 = TimeSpan.FromMinutes(5), DeviceAutoCheck15 = TimeSpan.FromMinutes(15);

    /// How often the service looks at which devices' periodic check is due, and how long after start-up the first check waits (the window is up by then).
    public static readonly TimeSpan DeviceAutoCheckTick = TimeSpan.FromMinutes(1), DeviceStartCheckDelay = TimeSpan.FromSeconds(4);

    /// A device's state read longer ago than this is only the last one known: its tray tile is faded with a clock.
    public static readonly TimeSpan DeviceStateStale = TimeSpan.FromMinutes(30);
}
