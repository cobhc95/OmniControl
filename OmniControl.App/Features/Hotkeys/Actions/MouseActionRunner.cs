using System.ComponentModel;
using System.Diagnostics;
using OmniControl.App.Features.Hotkeys;
using OmniControl.App.Features.Input.Mouse;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Features.Hotkeys.Actions;

/// Carries out one stored action (see <see cref="MouseActionCatalog"/>) with its options. Runs off the hook thread; problems go to the status line.
public sealed class MouseActionRunner
{
    private const string MagnifierProcess = "Magnify";
    private static readonly ushort[] CopyKeys = KeyChord.TryParse("Ctrl+C", out var keys) ? keys : Array.Empty<ushort>();

    private static readonly Dictionary<string, string> SearchAddresses = new()
    {
        ["Google"] = "https://www.google.com/search?q=",
        ["Bing"] = "https://www.bing.com/search?q=",
        ["DuckDuckGo"] = "https://duckduckgo.com/?q=",
    };

    private readonly Func<string, RunSettings?, bool> _runOmniAction;
    private readonly Action<string> _report;
    private readonly Action<string> _log;
    private readonly Func<bool>? _toRemote;
    private readonly Func<string, string?>? _keyEquivalentOf;
    private readonly LaunchLimiter _launches = new();   // the latest programs or folders started
    private readonly LaunchLimiter _keySends = new(Timing.KeySendBurstMax, Timing.KeySendBurstWindow);
    private int _winKeyNoted;                            // the mouse log said once that a windowed Remote Desktop may keep Windows-key shortcuts here

    /// <param name="runOmniAction">Runs the OmniControl hotkey action with this id (with the settings the input chose: a program to start, how far a level moves, a power countdown); false when there is none.</param>
    /// <param name="report">Shows a short problem message to the user.</param>
    /// <param name="log">Writes a line to the mouse log (what was started, and what was refused).</param>
    /// <param name="toRemote">True while actions go to a remote-input window in front as keys (<see cref="RemoteInputApps"/>); null: every action runs here (rules).</param>
    /// <param name="keyEquivalentOf">The master action's <see cref="HotkeyAction.KeyEquivalent"/> by id, or null.</param>
    public MouseActionRunner(Func<string, RunSettings?, bool> runOmniAction, Action<string> report, Action<string> log,
        Func<bool>? toRemote = null, Func<string, string?>? keyEquivalentOf = null)
    {
        _runOmniAction = runOmniAction;
        _report = report;
        _log = log;
        _toRemote = toRemote;
        _keyEquivalentOf = keyEquivalentOf;
    }

    /// Runs the action as many times as its "times" option says (once unless the action has that option set).
    public void Run(MouseAction action, MouseEvent at)
    {
        for (var i = action.Options.Int(MouseOptions.Times); i > 0; i--) RunOnce(action, at);
    }

    private void RunOnce(MouseAction action, MouseEvent at)
    {
        var id = action.Id;
        if (MouseActionCatalog.KeyTextOf(id) is { } keyText)
        {
            // A keystroke that opens a window (Win+E, Win+R...) counts as a launch, so it cannot flood the desktop either.
            if (!MouseActionCatalog.OpensWindow(id) || MayLaunch(keyText)) SendKeys(keyText);
            return;
        }
        if (MouseActionCatalog.RunTextOf(id) is { } target) { Launch(target); return; }
        if (SentToRemote(action)) return;

        var activeWindow = action.Options.Text(MouseOptions.Target) == MouseOptions.ActiveWindow;
        var pages = action.Options.Bool(MouseOptions.ByPages);

        switch (id)
        {
            case "click.left": InputSender.Click(MouseButton.Left); break;
            case "click.right": InputSender.Click(MouseButton.Right); break;
            case "click.middle": InputSender.Click(MouseButton.Middle); break;
            case "click.double": InputSender.Click(MouseButton.Left, twice: true); break;
            case "click.back": InputSender.Click(MouseButton.Back); break;
            case "click.forward": InputSender.Click(MouseButton.Forward); break;
            case "scroll.up": InputSender.Scroll(1); break;
            case "scroll.down": InputSender.Scroll(-1); break;
            case "scroll.left": InputSender.Scroll(-1, horizontal: true); break;
            case "scroll.right": InputSender.Scroll(1, horizontal: true); break;
            case "scrollwin.up": WindowCommands.ScrollWindow(at.X, at.Y, vertical: true, forward: false, pages); break;
            case "scrollwin.down": WindowCommands.ScrollWindow(at.X, at.Y, vertical: true, forward: true, pages); break;
            case "scrollwin.left": WindowCommands.ScrollWindow(at.X, at.Y, vertical: false, forward: false, pages); break;
            case "scrollwin.right": WindowCommands.ScrollWindow(at.X, at.Y, vertical: false, forward: true, pages); break;
            case "window.minimize": WindowCommands.Minimize(at.X, at.Y, activeWindow); break;
            case "window.maximize": WindowCommands.ToggleMaximize(at.X, at.Y, activeWindow); break;
            case "window.restore": WindowCommands.Restore(at.X, at.Y, activeWindow); break;
            case "window.close": WindowCommands.Close(at.X, at.Y, activeWindow); break;
            case "window.topmost": WindowCommands.ToggleTopmost(at.X, at.Y, activeWindow); break;
            case "power.monitoroff": SystemCommands.TurnMonitorsOff(); break;
            case "power.screensaver": SystemCommands.StartScreensaver(); break;
            case MouseActionCatalog.CycleCursor: CursorSpeed.Cycle(action.Options.Text(MouseOptions.CycleSpeeds)); break;
            case MouseActionCatalog.Magnifier: ToggleMagnifier(); break;
            case MouseActionCatalog.WebSearch: SearchSelectedText(action.Options); break;
            case MouseActionCatalog.Keys: SendKeys(action.Param); break;
            case MouseActionCatalog.Folder: Launch(action.Param); break;
            case MouseActionCatalog.Run:
                Launch(action.Param, action.Options.Text(MouseOptions.Arguments), action.Options.Text(MouseOptions.StartIn),
                    action.Options.Text(MouseOptions.WindowStyle), action.Options.Bool(MouseOptions.Administrator));
                break;
            case MouseActionCatalog.Omni:
                if (!_runOmniAction(action.Param, MouseOptions.RunOf(action.Options))) _report("That OmniControl action is no longer available. Pick another for this button, gesture or rule.");
                break;
        }
    }

    /// Sends the action as its shortcut when it has one and a remote-input window is in front, so the other computer acts on it. True when sent.
    private bool SentToRemote(MouseAction action)
    {
        if (_toRemote?.Invoke() != true) return false;
        var keys = action.Id == MouseActionCatalog.Omni ? _keyEquivalentOf?.Invoke(action.Param) : MouseActionCatalog.KeyEquivalentOf(action.Id);
        if (keys is null || RemoteInputApps.InFront() is not { } app) return false;
        _log($"{(action.Id == MouseActionCatalog.Omni ? action.Param : action.Id)} goes as {keys} to {app.Name}, in front");
        SendKeys(keys);
        return true;
    }

    /// Windows' Magnifier has no on/off call: Win+Plus starts it and Win+Esc closes it, so look at whether it is running.
    private void ToggleMagnifier() => SendKeys(Process.GetProcessesByName(MagnifierProcess).Length > 0 ? "Win+Esc" : "Win+Plus");

    /// Copies the selected text, then opens a web search for it in the default browser.
    private void SearchSelectedText(OptionValues options)
    {
        var engine = options.Text(MouseOptions.Engine);
        var address = engine == MouseOptions.CustomEngine ? options.Text(MouseOptions.CustomAddress) : SearchAddresses[engine];
        if (address.Length == 0)
        {
            _report("Web search needs an address: open its options (the cogwheel) and fill in the custom address.");
            return;
        }

        InputSender.Chord(CopyKeys);
        Thread.Sleep(Timing.MouseCopyWait); // the program needs a moment to put the selection on the clipboard
        if (ClipboardText.Read()?.Trim() is not { Length: > 0 } text) return;
        Launch(address + Uri.EscapeDataString(text));
    }

    /// Sends the extra shortcut an action was set to send along with itself, if any.
    public void SendAlongside(MouseAction action)
    {
        var text = action.Options.Text(MouseOptions.AlsoSend);
        if (text.Length > 0) SendKeys(text);
    }

    /// Sends one shortcut or a short sequence of them ("Ctrl+K, Ctrl+C"), in order, <see cref="Timing.MouseActionGap"/> apart; each one is
    /// pressed and released in one go, so nothing is left down.
    private void SendKeys(string text)
    {
        if (!KeyChord.TryParseSequence(text, out var chords))
        {
            _report(MouseOptions.KeysProblem(text) ?? "A button, gesture or rule has no keys to send: type them in its box.");
            return;
        }
        var steps = KeyChord.Steps(text);
        for (var i = 0; i < chords.Count; i++)
        {
            if (i > 0) Thread.Sleep(Timing.MouseActionGap); // a worker thread: the runner never runs on the hook or UI thread
            if (!_keySends.TryAcquire())
            {
                _log($"refused to send \"{text}\": too many shortcuts in a row");
                _report("Stopped a button, gesture or rule from sending keys again and again. Check its settings.");
                return;
            }
            if (_toRemote?.Invoke() == true && RemoteInputApps.KeysFor(chords[i]) is { } remote) TypeToRemote(remote, steps[i]);
            else InputSender.Chord(chords[i]);
        }
    }

    /// Types the keys into the remote-input window in front, as a keyboard would, and logs exactly what was sent; a Windows-key shortcut
    /// the window keeps on this PC has no replacement and is typed as it is, said once in the mouse log.
    private void TypeToRemote(RemoteInputApps.RemoteKeys remote, string text)
    {
        var sent = InputSender.Type(remote.Keys);
        _log($"{text}{(remote.SentAs is { } swapped ? $" as its replacement {swapped}" : string.Empty)} typed into {remote.App.Name}: {sent}");
        if (remote.KeptHere && Interlocked.Exchange(ref _winKeyNoted, 1) == 0)
            _log($"{remote.App.Name} keeps Windows-key shortcuts on this PC now (not full screen, or \"Apply Windows key combinations\" is not \"On the remote computer\"): {text} acts here");
    }

    /// Allows at most <see cref="Timing.LaunchBurstMax"/> launches (programs, folders, and keystrokes that open a window) per
    /// <see cref="Timing.LaunchBurstWindow"/>; the rest are refused and reported.
    private bool MayLaunch(string path)
    {
        if (!_launches.TryAcquire())
        {
            _log($"refused to start \"{path}\": too many launches in a row");
            _report("Stopped a button, gesture or rule from opening programs or folders again and again. Check its settings.");
            return false;
        }
        _log($"starting \"{path}\"");
        return true;
    }

    private void Launch(string path, string arguments = "", string startIn = "", string windowStyle = "Normal", bool administrator = false)
    {
        if (!MayLaunch(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true,
                Arguments = arguments,
                WorkingDirectory = startIn,
                WindowStyle = windowStyle switch
                {
                    "Minimised" => ProcessWindowStyle.Minimized,
                    "Maximised" => ProcessWindowStyle.Maximized,
                    "Hidden" => ProcessWindowStyle.Hidden,
                    _ => ProcessWindowStyle.Normal,
                },
                Verb = administrator ? "runas" : string.Empty,
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            _report($"Couldn't start \"{path}\": {ex.Message}");
        }
    }
}
