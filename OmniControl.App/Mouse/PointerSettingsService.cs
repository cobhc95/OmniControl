using Avalonia.Threading;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Pointer;

namespace OmniControl.App.Mouse;

/// <summary>
/// Windows' own pointer settings, changed live from OmniControl. Every read asks Windows (nothing is remembered as the truth); writes
/// go through a <see cref="LevelStepper"/> on a worker thread, newest value only, because each one announces the change to every window.
/// Before the first change the user's original values are kept (<see cref="PointerBackup"/>). Swapping the mouse buttons is a trial:
/// it is put back after <see cref="Timing.PointerSwapKeep"/> unless kept, at exit, and at the next start after a crash (<see cref="SwapTrial"/>).
/// </summary>
public sealed class PointerSettingsService : IDisposable
{
    private const int Off = 0, On = 1;

    private readonly PointerSettings _windows = new();
    private readonly LevelStepper _writes = new();
    private readonly PointerBackup _backup = new();
    private readonly DispatcherTimer _trialTimer = new() { Interval = Timing.PointerSwapKeep };
    private CancellationTokenSource? _trial;
    private bool _swappedBefore;

    /// <summary>Raised on the UI thread when Windows refused a write; the cards then show the real value again.</summary>
    public event Action<PointerValue>? WriteFailed;

    /// <summary>Raised on the UI thread when OmniControl changed the swap on its own (a trial put back), so the page shows the real state.</summary>
    public event Action? SwapChanged;

    /// <summary>True once the user's original values are kept, so they can be restored.</summary>
    public bool HasOriginal => _backup.Original is not null;

    public PointerSettingsService()
    {
        // A previous run ended during a swap trial: the swap was never kept, so it goes back.
        if (SwapTrial.TakeLeftOver() is { } before) _windows.Write(PointerValue.Swap, before ? On : Off);
        _trialTimer.Tick += (_, _) => PutBackSwap();
    }

    /// <summary>The value Windows has now, or null when Windows won't say.</summary>
    public int? Read(PointerValue value) => _windows.Read(value);

    /// <summary>Sets a value (a card was moved). Not the swap, which is always a trial (<see cref="ChangeSwap"/>).</summary>
    public void Set(PointerValue value, int wanted)
    {
        KeepOriginal();
        _writes.Set(value.ToString(), PointerValues.Info(value).Clamp(wanted), v => Write(value, v));
    }

    /// <summary>Moves a value by <paramref name="delta"/> within Windows' range (an action) and returns the value it goes to, or null when Windows won't say.</summary>
    public int? Step(PointerValue value, int delta)
    {
        KeepOriginal();
        var info = PointerValues.Info(value);
        return _writes.Step(value.ToString(), delta, () => _windows.Read(value), v => Write(value, v), null, info.Min, info.Max);
    }

    /// <summary>
    /// Swaps the buttons (or not) as a trial and returns a token that is cancelled when the trial ends (kept, put back, or out of time);
    /// null when nothing had to change. Asking for the state from before a running trial puts it back.
    /// </summary>
    public CancellationToken? ChangeSwap(bool swapped)
    {
        if (_trial is not null)
        {
            if (swapped == _swappedBefore) PutBackSwap();
            return null;
        }
        var now = _windows.Read(PointerValue.Swap) == On;
        if (swapped == now) return null;

        KeepOriginal();
        _swappedBefore = now;
        SwapTrial.Begin(now);
        _trial = new CancellationTokenSource();
        _trialTimer.Start();
        WriteSwap(swapped, persist: false); // this session only until kept, so even a lost journal ends at sign-out
        return _trial.Token;
    }

    /// <summary>The user chose to keep the swap on trial: it is saved like any other setting.</summary>
    public void KeepSwap()
    {
        if (_trial is null) return;
        WriteSwap(!_swappedBefore, persist: true);
        EndTrial();
    }

    /// <summary>Puts a swap on trial back.</summary>
    public void PutBackSwap()
    {
        if (_trial is null) return;
        WriteSwap(_swappedBefore, persist: true);
        EndTrial();
    }

    /// <summary>Windows' cursor schemes and the user's own, as Windows has them now.</summary>
    public IReadOnlyList<CursorScheme> Schemes() => CursorSchemes.List();

    /// <summary>The scheme whose pointers are all in force now, or null when pointers were changed one by one.</summary>
    public CursorScheme? CurrentScheme(IReadOnlyList<CursorScheme> schemes) => CursorSchemes.Current(schemes);

    /// <summary>The file one pointer uses now ("" = Windows' built-in pointer).</summary>
    public string PointerFile(CursorRole role) => CursorSchemes.FileOf(role);

    /// <summary>Makes <paramref name="scheme"/> the pointers in force. False when Windows refused.</summary>
    public Task<bool> ApplyScheme(CursorScheme scheme)
    {
        KeepOriginal();
        return Task.Run(() => CursorSchemes.Apply(scheme));
    }

    /// <summary>Why <paramref name="file"/> can't be used as a pointer (one plain sentence), or null when it can.</summary>
    public string? ProblemWith(string file) => CursorSchemes.Problem(file);

    /// <summary>Uses <paramref name="file"/> for one pointer, or Windows' default pointer for it when null. False when Windows refused.</summary>
    public Task<bool> SetPointer(CursorRole role, string? file)
    {
        KeepOriginal();
        var index = CursorRoles.All.ToList().IndexOf(role);
        var chosen = file ?? CursorSchemes.WindowsDefault(Schemes())?.Files[index] ?? string.Empty;
        return Task.Run(() => CursorSchemes.SetRole(role, chosen));
    }

    /// <summary>
    /// Every setting back to Windows' default and Windows' default pointers, except the button swap, which the caller changes as a
    /// trial (<see cref="ChangeSwap"/>). False when Windows refused any of it.
    /// </summary>
    public Task<bool> ResetToDefaults()
    {
        KeepOriginal();
        PutBackSwap();
        var defaults = PointerValues.All.Where(v => v != PointerValue.Swap).ToDictionary(v => v, v => PointerValues.Info(v).Default);
        var scheme = CursorSchemes.WindowsDefault(Schemes());
        return Task.Run(() => WriteAll(defaults) & (scheme is null ? CursorSchemes.ApplyNone() : CursorSchemes.Apply(scheme)));
    }

    /// <summary>Every setting and pointer back to what the user had before OmniControl's first change (the swap too, without a trial). False when Windows refused any of it.</summary>
    public Task<bool> RestoreOriginal() => _backup.Original is { } original ? RestoreTo(original) : Task.FromResult(true);

    /// <summary>The values and pointers Windows has now, as a copy <see cref="RestoreTo"/> can put back ("Restore defaults" keeps one for its undo).</summary>
    public PointerOriginal Capture() => new(
        PointerValues.All.Select(v => (v, _windows.Read(v))).Where(p => p.Item2 is not null).ToDictionary(p => p.v.ToString(), p => p.Item2!.Value),
        CursorSchemes.Snapshot(), DateTime.Now);

    /// <summary>What the user had before OmniControl's first change, or null when it has changed nothing.</summary>
    public PointerOriginal? Original => _backup.Original;

    /// <summary>Every setting and pointer in <paramref name="state"/> back in Windows (the swap too, without a trial). False when Windows refused any of it.</summary>
    public Task<bool> RestoreTo(PointerOriginal state)
    {
        if (_trial is not null) EndTrial(); // the restore writes the swap itself
        var values = PointerValues.All.Where(v => state.Values.ContainsKey(v.ToString()))
            .ToDictionary(v => v, v => state.Values[v.ToString()]);
        return Task.Run(() => WriteAll(values) & CursorSchemes.Restore(state.Cursors));
    }

    /// <summary>Worker thread: writes each value in restore order and tells the stepper where they now are. True when Windows took them all.</summary>
    private bool WriteAll(IReadOnlyDictionary<PointerValue, int> values)
    {
        var ok = true;
        foreach (var value in PointerValues.All.Where(values.ContainsKey))
        {
            ok &= _windows.Write(value, values[value]);
            _writes.Remember(value.ToString(), values[value]);
        }
        return ok;
    }

    private void KeepOriginal() => _backup.EnsureSaved(Capture);

    /// <summary>Worker thread: one write; a refusal is reported on the UI thread.</summary>
    private void Write(PointerValue value, int wanted, bool persist = true)
    {
        if (!_windows.Write(value, wanted, persist)) Dispatcher.UIThread.Post(() => WriteFailed?.Invoke(value));
    }

    private void WriteSwap(bool swapped, bool persist) => _writes.Set(nameof(PointerValue.Swap), swapped ? On : Off, v =>
    {
        Write(PointerValue.Swap, v, persist);
        Dispatcher.UIThread.Post(() => SwapChanged?.Invoke());
    });

    private void EndTrial()
    {
        _trialTimer.Stop();
        SwapTrial.End();
        var trial = _trial;
        _trial = null;
        trial?.Cancel();
        trial?.Dispose();
    }

    /// <summary>At exit a swap that was never kept goes back, at once, on this thread.</summary>
    public void Dispose()
    {
        _trialTimer.Stop();
        if (_trial is null) return;
        _windows.Write(PointerValue.Swap, _swappedBefore ? On : Off);
        SwapTrial.End();
        _trial.Dispose();
        _trial = null;
    }
}
