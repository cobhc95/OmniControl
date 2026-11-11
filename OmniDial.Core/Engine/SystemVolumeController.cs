using OmniDial.Core.Interfaces;
using OmniDial.Core.Models;
#if WINDOWS
using System.Runtime.InteropServices;
#endif

namespace OmniDial.Core.Engine;

/// <summary>System volume — Core Audio on Windows, pactl/amixer stub on Linux.</summary>
public sealed class SystemVolumeController : ISystemVolume
{
#if WINDOWS
    public OperationResult GetVolume(out int percent)
    {
        percent = 50;
        try
        {
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IntPtr devicePtr);
            var device = (IMMDevice)Marshal.GetObjectForIUnknown(devicePtr);
            device.Activate(typeof(IAudioEndpointVolume).GUID, CLSCTX.ALL, IntPtr.Zero, out IntPtr volPtr);
            var volume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(volPtr);
            volume.GetMasterVolumeLevelScalar(out float scalar);
            percent = (int)Math.Round(scalar * 100);
            Release(volPtr); Release(devicePtr); ReleaseCom(enumerator);
            return OperationResult.Ok($"System volume {percent}%");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Core Audio: {ex.Message}");
        }
    }

    public OperationResult SetVolume(int percent)
    {
        try
        {
            float scalar = Math.Clamp(percent, 0, 100) / 100f;
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out IntPtr devicePtr);
            var device = (IMMDevice)Marshal.GetObjectForIUnknown(devicePtr);
            device.Activate(typeof(IAudioEndpointVolume).GUID, CLSCTX.ALL, IntPtr.Zero, out IntPtr volPtr);
            var volume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(volPtr);
            volume.SetMasterVolumeLevelScalar(scalar, Guid.NewGuid());
            Release(volPtr); Release(devicePtr); ReleaseCom(enumerator);
            return OperationResult.Ok($"System volume {percent}%");
        }
        catch (Exception ex)
        {
            return OperationResult.Fail($"Core Audio: {ex.Message}");
        }
    }

    private static void Release(IntPtr p) { if (p != IntPtr.Zero) Marshal.Release(p); }
    private static void ReleaseCom(object o) { if (o != null) Marshal.ReleaseComObject(o); }

    private enum EDataFlow { eRender = 0, eCapture = 1 }
    private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }
    private enum CLSCTX : uint { ALL = 0x17 }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int QueryInterface(ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int AddRef();
        [PreserveSig] int Release();
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IntPtr device);
    }

    [ComImport, Guid("0BD7A4BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int QueryInterface(ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int AddRef();
        [PreserveSig] int Release();
        [PreserveSig] int Activate(Guid iid, CLSCTX dwClsCtx, IntPtr pActivationParams, out IntPtr ppInterface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int QueryInterface(ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int AddRef();
        [PreserveSig] int Release();
        [PreserveSig] int RegisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr pNotify);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float pfLevel);
        [PreserveSig] int SetMasterVolumeLevelScalar(float fLevel, [MarshalAs(UnmanagedType.LPStruct)] Guid pguidLc);
    }
#else
    public OperationResult GetVolume(out int percent)
    {
        percent = 50;
        return OperationResult.Fail("Linux system volume: implement via PipeWire/PulseAudio (pactl).");
    }
    public OperationResult SetVolume(int percent) =>
        OperationResult.Fail("Linux system volume: implement via pactl set-sink-volume @DEFAULT_SINK@.");
#endif
}
