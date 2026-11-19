namespace OmniControl.Core.Engine;

/// Choosing the playback device per program.
public static partial class CoreAudio
{
    // ---- per-program output device (undocumented Windows 10/11 policy-config interface; the same one Settings uses) ----

    /// Sends a program's audio to <paramref name="endpointId"/> (its playback endpoint id), or back to the default device when it is
    /// null or empty, through the processes that own its sound sessions. Windows applies it to streams the program opens afterwards
    /// (measured on build 26300: a stream already open stays where it is). A process that is no longer running is skipped: Windows
    /// answers E_INVALIDARG (0x80070057) for one, and sessions of a closed process can still be listed. Windows keeps the choice per
    /// program (.exe), so when it refuses the process that plays (a browser plays from a child process) the topmost process of the
    /// same .exe is asked instead; such a refusal has not been seen with the correct path.
    /// Returns false with a short reason (no final full stop) only when a running process was refused both ways.
    public static bool TrySetProgramDevice(IEnumerable<int> processIds, string? endpointId, out string error)
    {
        error = string.Empty;
        foreach (var pid in processIds.Distinct())
        {
            if (!ProcessTree.IsRunning(pid) || TrySetAppDevice(pid, endpointId, out var refused)) continue;
            var root = ProcessTree.Roots(new[] { pid })[0];
            if (root != pid && TrySetAppDevice(root, endpointId, out _)) continue;
            if (!ProcessTree.IsRunning(pid)) continue; // it closed while being moved
            error = refused;
            return false;
        }
        return true;
    }

    /// DEVINTERFACE_AUDIO_RENDER (KSCATEGORY_RENDER), the device interface class of every playback endpoint.
    private const string AudioRenderInterface = "{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    /// The playback endpoint's device interface path, the form the per-program policy takes. A wrong path (the class id once ended in
    /// ...79d4 here) makes Windows answer 0x80070057 for every device while clearing still works.
    public static string RenderInterfacePath(string endpointId) => $@"\\?\SWD#MMDEVAPI#{endpointId}#{AudioRenderInterface}";

    /// One process; false with a short reason (no final full stop) when Windows doesn't support it or refuses.
    private static unsafe bool TrySetAppDevice(int pid, string? endpointId, out string error)
    {
        error = string.Empty;
        IntPtr factory = IntPtr.Zero, className = IntPtr.Zero, device = IntPtr.Zero;
        try
        {
            if (WindowsCreateString("Windows.Media.Internal.AudioPolicyConfig", 40, out className) != 0)
            {
                error = "Windows audio policy is unavailable";
                return false;
            }
            var iid = new Guid("ab3d4648-e242-459f-b02f-541c70306324");
            if (RoGetActivationFactory(className, ref iid, out factory) != 0 || factory == IntPtr.Zero)
            {
                error = "This Windows version doesn't support choosing a device per program";
                return false;
            }

            if (!string.IsNullOrEmpty(endpointId))
            {
                var path = RenderInterfacePath(endpointId);
                if (WindowsCreateString(path, path.Length, out device) != 0) { error = "Couldn't prepare the device"; return false; }
            }

            // Slot 25: after IUnknown/IInspectable (6) and 19 undocumented methods.
            var table = *(IntPtr**)factory;
            var set = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr, int>)table[25];
            var hr1 = set(factory, (uint)pid, Render, Console, device);
            var hr2 = set(factory, (uint)pid, Render, Multimedia, device);
            if (hr1 != 0 || hr2 != 0) { error = $"Windows refused the change, code 0x{(hr1 != 0 ? hr1 : hr2):X8}"; return false; }
            return true;
        }
        catch (Exception ex) when (ex is COMException or SEHException)
        {
            // An undocumented interface: a Windows build that changed it must not take the app down.
            error = ex.Message.TrimEnd('.');
            return false;
        }
        finally
        {
            if (factory != IntPtr.Zero) Marshal.Release(factory);
            if (device != IntPtr.Zero) WindowsDeleteString(device);
            if (className != IntPtr.Zero) WindowsDeleteString(className);
        }
    }
}
