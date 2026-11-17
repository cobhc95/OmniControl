using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.Win32.Input;
using OmniControl.App.Hotkeys;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Keyboard;

namespace OmniControl.App.Tray;

/// <summary>
/// The "Use for Win+A" switch of the quick panel's action: the one owner of its state (an app setting, off by default). While on, a keyboard
/// hook (<see cref="WinKeyHook"/>) takes Win+A from Windows and runs the panel's action by id through the one run path; off, Win+A is Windows'.
/// </summary>
public sealed class WinAKey : IDisposable
{
    public const string OptionKey = "wina";

    private readonly WinKeyHook _hook = new((ushort)KeyInterop.VirtualKeyFromKey(Key.A));
    private readonly string _actionId;
    private readonly Func<string, bool> _run;
    private readonly Action<string> _report;

    public WinAKey(string actionId, Func<string, bool> run, Action<string> report)
    {
        (_actionId, _run, _report) = (actionId, run, report);
        _hook.Pressed += OnPressed;
        if (!StartupMode.Inert && IsOn) Apply();
    }

    public bool IsOn => AppSettings.Current.WinAForPanel;

    /// <summary>Turns the switch on or off at once and saves it; says in one line when Windows refused the hook.</summary>
    public void Set(bool on)
    {
        var settings = AppSettings.Current;
        settings.WinAForPanel = on;
        settings.Save();
        Apply();
    }

    private void Apply()
    {
        if (!IsOn) _hook.Stop();
        else if (!_hook.Start()) _report("Couldn't take Win+A from Windows.");
    }

    // The hook answers on a pool thread; the action runs on the UI thread like every other.
    private void OnPressed() => Dispatcher.UIThread.Post(() => _run(_actionId));

    public void Dispose()
    {
        _hook.Pressed -= OnPressed;
        _hook.Dispose();
    }
}
