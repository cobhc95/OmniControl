using System.Collections.Concurrent;

namespace OmniControl.Core.Engine.Trackpad;

/// The shell's own copy of the three- and four-finger settings. The shell (<c>twinui.dll</c> in explorer.exe) reads them from the registry
/// into a cache when it starts and does not watch the key: it reads them again only when told to through its touchpad settings service,
/// which is what the Settings app does after writing a value.
/// The service is an undocumented interface of the immersive shell, reached as the Settings app reaches it: the immersive shell's
/// IServiceProvider, then QueryService for the service. Its slot 3 takes a setting's id and makes the shell refresh its cache; slot 5
/// returns the value the shell uses now for an id. Slot 4 is never called (its meaning is unknown).
/// Every call goes into explorer.exe, so it runs on one worker thread of its own (made at the first call and kept, in the multithreaded
/// apartment the service wants) and is waited for at most <see cref="CoreTiming.ShellCallTimeoutMs"/>: a busy or hung Explorer never holds
/// the caller (the touchpad queue, the UI thread). While a call that did not come back in time is still out, no new one is made; a call
/// slower than <see cref="CoreTiming.ShellCallSlowMs"/> is logged, naming it.
internal sealed class ShellTouchpadSettings
{
    private readonly Action<string>? _log;
    private readonly BlockingCollection<Action> _calls = new();
    private readonly Lazy<Thread> _worker;
    private volatile bool _late; // a call did not come back in time and is still out

    public ShellTouchpadSettings(Action<string>? log)
    {
        _log = log;
        _worker = new Lazy<Thread>(() =>
        {
            var thread = new Thread(Work) { IsBackground = true, Name = "Shell touchpad calls" };
            thread.SetApartmentState(ApartmentState.MTA);
            thread.Start();
            return thread;
        });
    }

    /// The worker: one call at a time, for the life of the program (a background thread, it ends with it).
    private void Work()
    {
        foreach (var call in _calls.GetConsumingEnumerable())
        {
            call();
            _late = false;
        }
    }

    private static readonly Guid ImmersiveShell = new("c2f03a33-21f5-47fa-b4bb-156362a2f239");
    private static readonly Guid ServiceId = new("53660488-8855-460b-a9ab-5cfc6b5012ca");
    private static readonly Guid ServiceInterface = new("4214f6fa-eb36-4e2f-9ca2-23fdc1832df7");

    [ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(ref Guid service, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
    }

    [ComImport, Guid("4214f6fa-eb36-4e2f-9ca2-23fdc1832df7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITouchpadSettingsService
    {
        /// Slot 3: the setting with this id was written; the shell refreshes its cache.
        [PreserveSig] int SettingChanged(int id);

        /// Slot 4: placeholder that keeps the slots in order; never called.
        [PreserveSig] int Unused(int id);

        /// Slot 5: the value the shell uses now for this id.
        [PreserveSig] int GetValue(int id, out int value);
    }

    /// Tells the shell that the setting with this id was written. Returns null when the shell took the call, or one plain sentence.
    public string? Refresh(int id) => Run($"refresh setting {id}", service => service.SettingChanged(id));

    /// The value the shell acts on now for this id. Returns null when it answered, or one plain sentence.
    public string? Read(int id, out int value)
    {
        var read = 0;
        var problem = Run($"read setting {id}", service => service.GetValue(id, out read));
        value = problem is null ? read : 0;
        return problem;
    }

    private string? Run(string what, Func<ITouchpadSettingsService, int> call)
    {
        if (_late) return "Windows' shell is still busy with an earlier touchpad call.";
        string? problem = null;
        var done = new ManualResetEventSlim(); // not disposed: a late call still sets it after the wait gave up
        var started = Environment.TickCount64;
        _ = _worker.Value;
        _calls.Add(() =>
        {
            problem = Use(call);
            done.Set();
        });
        if (!done.Wait(CoreTiming.ShellCallTimeoutMs))
        {
            _late = true;
            if (done.IsSet) _late = false; // it came back just now, after the worker had already cleared the mark
            _log?.Invoke($"Explorer's touchpad settings service did not answer \"{what}\" within {CoreTiming.ShellCallTimeoutMs} ms; no new call is made until it does.");
            return "Windows' shell didn't answer in time.";
        }
        var took = Environment.TickCount64 - started;
        if (took > CoreTiming.ShellCallSlowMs) _log?.Invoke($"Explorer's touchpad settings service took {took} ms to \"{what}\".");
        return problem;
    }

    private static string? Use(Func<ITouchpadSettingsService, int> call)
    {
        object? shell = null, service = null;
        try
        {
            shell = Type.GetTypeFromCLSID(ImmersiveShell) is { } type ? Activator.CreateInstance(type) : null;
            if (shell is not IServiceProvider provider) return "Windows' shell isn't running.";
            var id = ServiceId;
            var iid = ServiceInterface;
            var found = provider.QueryService(ref id, ref iid, out service);
            if (found != 0 || service is not ITouchpadSettingsService settings)
                return $"Windows' shell doesn't offer its touchpad settings (0x{found:X8}).";
            var result = call(settings);
            return result == 0 ? null : $"Windows' shell refused the touchpad setting (0x{result:X8}).";
        }
        // Explorer restarting, not running, or a Windows version without this service: said in one sentence, the caller carries on.
        catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException)
        {
            return $"Windows' shell didn't answer ({e.Message}).";
        }
        finally
        {
            if (service is not null && Marshal.IsComObject(service)) Marshal.ReleaseComObject(service);
            if (shell is not null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
        }
    }
}
