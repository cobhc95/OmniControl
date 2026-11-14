using System.Runtime.InteropServices;

namespace OmniControl.Core.Engine.Trackpad;

/// <summary>
/// The shell's own copy of the three- and four-finger settings. The shell (<c>twinui.dll</c> in explorer.exe) reads them from the registry
/// into a cache when it starts and does not watch the key: it reads them again only when told to through its touchpad settings service,
/// which is what the Settings app does after writing a value (findings in HANDOVER.md, "Trackpad takeover: the shell's own copy").
/// The service is an undocumented interface of the immersive shell, reached as the Settings app reaches it: the immersive shell's
/// IServiceProvider, then QueryService for the service. Its slot 3 takes a setting's id and makes the shell refresh its cache; slot 5
/// returns the value the shell uses now for an id. Slot 4 is never called (its meaning is unknown).
/// </summary>
internal static class ShellTouchpadSettings
{
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
        /// <summary>Slot 3: the setting with this id was written; the shell refreshes its cache.</summary>
        [PreserveSig] int SettingChanged(int id);

        /// <summary>Slot 4: placeholder that keeps the slots in order; never called.</summary>
        [PreserveSig] int Unused(int id);

        /// <summary>Slot 5: the value the shell uses now for this id.</summary>
        [PreserveSig] int GetValue(int id, out int value);
    }

    /// <summary>Tells the shell that the setting with this id was written. Returns null when the shell took the call, or one plain sentence.</summary>
    public static string? Refresh(int id) => Use(service => service.SettingChanged(id));

    /// <summary>The value the shell acts on now for this id. Returns null when it answered, or one plain sentence.</summary>
    public static string? Read(int id, out int value)
    {
        var read = 0;
        var problem = Use(service => service.GetValue(id, out read));
        value = read;
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
