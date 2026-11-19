using Avalonia.Input;
using Avalonia.Win32.Input;
using OmniControl.App.Features.Hotkeys;
using OmniControl.Core.Engine.Keyboard;

namespace OmniControl.App.Features.Tray;

/// The "Use for Win+A" switch of the quick panel's action: the one owner of its state (an app setting, off by default). While on, a keyboard
/// hook (<see cref="WinKeyHook"/>) takes Win+A from Windows and runs the panel's action by id through the one run path; off, Win+A is Windows'.
public sealed class WinAKey : IDisposable
{
    public const string OptionKey = "wina";

    private readonly WinKeyHook _hook = new((ushort)KeyInterop.VirtualKeyFromKey(Key.A));
    private readonly string _actionId;
    private readonly Func<string, bool> _run;
    private readonly Action<string> _report;
    private readonly ISettingSlot<bool> _on;

    /// <param name="on">The setting that keeps the switch.</param>
    public WinAKey(string actionId, Func<string, bool> run, Action<string> report, ISettingSlot<bool> on)
    {
        (_actionId, _run, _report, _on) = (actionId, run, report, on);
        _hook.Pressed += OnPressed;
        if (IsOn) Apply();
    }

    public bool IsOn => _on.Value;

    /// Turns the switch on or off at once and saves it; says in one line when Windows refused the hook.
    public void Set(bool on)
    {
        _on.Value = on;
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
