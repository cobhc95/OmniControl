using System.Runtime.InteropServices;
using System.Text;

namespace OmniControl.Core.Engine;

/// <summary>Choosing the playback device per program.</summary>
public static partial class CoreAudio
{
    // ---- per-program output device (undocumented Windows 10/11 policy-config interface; the same one Settings uses) ----

    /// <summary>
    /// Sends a program's audio to <paramref name="endpointId"/>, or back to the default device when it is null or empty.
    /// Windows applies it to streams the program opens afterwards. Returns false with a reason when Windows doesn't support it.
    /// </summary>
    public static unsafe bool TrySetAppDevice(int pid, string? endpointId, out string error)
    {
        error = string.Empty;
        IntPtr factory = IntPtr.Zero, className = IntPtr.Zero, device = IntPtr.Zero;
        try
        {
            if (WindowsCreateString("Windows.Media.Internal.AudioPolicyConfig", 40, out className) != 0)
            {
                error = "Windows audio policy is unavailable.";
                return false;
            }
            var iid = new Guid("ab3d4648-e242-459f-b02f-541c70306324");
            if (RoGetActivationFactory(className, ref iid, out factory) != 0 || factory == IntPtr.Zero)
            {
                error = "This Windows version doesn't support choosing a device per program.";
                return false;
            }

            if (!string.IsNullOrEmpty(endpointId))
            {
                var path = $@"\\?\SWD#MMDEVAPI#{endpointId}#{{e6327cad-dcec-4949-ae8a-991e976a79d4}}";
                if (WindowsCreateString(path, path.Length, out device) != 0) { error = "Couldn't prepare the device."; return false; }
            }

            // Slot 25: after IUnknown/IInspectable (6) and 19 undocumented methods.
            var table = *(IntPtr**)factory;
            var set = (delegate* unmanaged[Stdcall]<IntPtr, uint, int, int, IntPtr, int>)table[25];
            var hr1 = set(factory, (uint)pid, Render, Console, device);
            var hr2 = set(factory, (uint)pid, Render, Multimedia, device);
            if (hr1 != 0 || hr2 != 0) { error = $"Windows refused the change (0x{(hr1 != 0 ? hr1 : hr2):X8})."; return false; }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
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
