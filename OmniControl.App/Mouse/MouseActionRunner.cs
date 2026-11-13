using System.ComponentModel;
using System.Diagnostics;
using OmniControl.App.Services;
using OmniControl.Core.Engine;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Mouse;

/// <summary>Carries out one stored action (see <see cref="MouseActionCatalog"/>) with its options. Runs off the hook thread; problems go to the status line.</summary>
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

    private readonly Func<string, bool> _runOmniAction;
    private readonly Action<string> _report;
    private readonly Action<string> _log;
    private readonly Queue<DateTime> _launches = new();   // when the latest programs or folders were started

    /// <param name="runOmniAction">Runs the OmniControl hotkey action with this id; false when there is none.</param>
    /// <param name="report">Shows a short problem message to the user.</param>
    /// <param name="log">Writes a line to the mouse log (what was started, and what was refused).</param>
    public MouseActionRunner(Func<string, bool> runOmniAction, Action<string> report, Action<string> log)
    {
        _runOmniAction = runOmniAction;
        _report = report;
        _log = log;
    }

    /// <summary>Runs the action as many times as its "times" option says (once unless the action has that option set).</summary>
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
                if (!_runOmniAction(action.Param)) _report("That OmniControl action is no longer available. Pick another for this button, gesture or rule.");
                break;
        }
    }

    /// <summary>Windows' Magnifier has no on/off call: Win+Plus starts it and Win+Esc closes it, so look at whether it is running.</summary>
    private void ToggleMagnifier() => SendKeys(Process.GetProcessesByName(MagnifierProcess).Length > 0 ? "Win+Esc" : "Win+Plus");

    /// <summary>Copies the selected text, then opens a web search for it in the default browser.</summary>
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

    /// <summary>Sends the extra shortcut an action was set to send along with itself, if any.</summary>
    public void SendAlongside(MouseAction action)
    {
        var text = action.Options.Text(MouseOptions.AlsoSend);
        if (text.Length > 0) SendKeys(text);
    }

    private void SendKeys(string text)
    {
        if (KeyChord.TryParse(text, out var keys)) InputSender.Chord(keys);
        else _report($"Couldn't read the shortcut \"{text}\". Use names like Ctrl+Shift+T.");
    }

    /// <summary>
    /// Allows at most <see cref="Timing.LaunchBurstMax"/> launches (programs, folders, and keystrokes that open a window) per
    /// <see cref="Timing.LaunchBurstWindow"/>; the rest are refused and reported.
    /// </summary>
    private bool MayLaunch(string path)
    {
        lock (_launches)
        {
            var now = DateTime.UtcNow;
            while (_launches.Count > 0 && now - _launches.Peek() > Timing.LaunchBurstWindow) _launches.Dequeue();
            if (_launches.Count >= Timing.LaunchBurstMax)
            {
                _log($"refused to start \"{path}\": too many launches in a row");
                _report("Stopped a button, gesture or rule from opening programs or folders again and again. Check its settings.");
                return false;
            }
            _launches.Enqueue(now);
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
