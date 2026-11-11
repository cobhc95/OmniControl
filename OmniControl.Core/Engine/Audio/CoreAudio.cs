using System.Runtime.InteropServices;
using System.Text;

namespace OmniControl.Core.Engine;

/// <summary>Windows Core Audio, split by concern: devices, sessions, master volume, per-program routing, interop (see the other CoreAudio.*.cs files).</summary>
public static partial class CoreAudio
{
    private static readonly Guid ClsidEnumerator = new("BCDE0395-E52F-467C-8E3D-C4579291692E");
    private static IMMDeviceEnumerator NewEnumerator() =>
        (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidEnumerator)!)!;

    private const int Render = 0, Capture = 1, Console = 0, Multimedia = 1, StateActive = 1, ClsCtxAll = 0x17;
    private static Guid _ctx = Guid.NewGuid();

}
