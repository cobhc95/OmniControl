using System.Runtime.CompilerServices;

namespace OmniControl.Core.Engine;

/// The host side of the VST2 interface: the callback a plug-in calls (audioMaster opcodes, answers as TabForge gives them) and reading
/// the texts it writes. The callback may run on any thread, the audio thread included, so it allocates nothing and never throws into
/// native code.
internal sealed unsafe partial class Vst2Effect
{
    /// The name OmniControl gives itself when a plug-in asks for the host's vendor or product (VST2 limits it to 64 bytes).
    private const string HostName = "OmniControl";
    private const int HostNameBytes = 64;

    /// What OmniControl tells a plug-in it can do (audioMasterCanDo): only resize the editor's window. NUL-separated ASCII.
    private static ReadOnlySpan<byte> HostCanDo => "sizeWindow\0"u8;

    /// The editor's size the plug-in asked for (audioMasterSizeWindow, any thread): width high, height low; read by the editor's idle call.
    private long _pendingSize;
    private int _sizePending;

    /// Takes the size the plug-in asked for since the last call, if any (UI thread).
    internal bool TakePendingSize(out int width, out int height)
    {
        var size = Interlocked.Read(ref _pendingSize);
        width = (int)(size >> 32);
        height = (int)(uint)size;
        return Interlocked.Exchange(ref _sizePending, 0) != 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr HostCallback(Vst2Abi.AEffect* effect, int opcode, int index, IntPtr value, IntPtr ptr, float opt)
    {
        try
        {
            switch (opcode)
            {
                case Vst2Abi.HostVersion: return Vst2Abi.HostVersionNumber;
                case Vst2Abi.HostCurrentId: return effect is null ? 0 : effect->uniqueID;
                case Vst2Abi.HostGetVendorString or Vst2Abi.HostGetProductString: return WriteText(ptr, HostName, HostNameBytes);
                case Vst2Abi.HostGetVendorVersion: return 1;
                case Vst2Abi.HostCanDo: return CanDo(ptr) ? 1 : 0;
                case Vst2Abi.HostGetLanguage: return Vst2Abi.LanguageEnglish;
                case Vst2Abi.HostIoChanged or Vst2Abi.HostUpdateDisplay: return 1; // the latency is read again on every look
            }
            // Only the calls that need the instance look it up: during its entry point a plug-in has not been given one yet.
            if (SelfOf(effect) is not { } self) return 0;
            switch (opcode)
            {
                case Vst2Abi.HostGetTime: return (IntPtr)self._time;
                case Vst2Abi.HostGetSampleRate: return self._sampleRate;
                case Vst2Abi.HostGetBlockSize: return self.MaxBlockFrames;
                case Vst2Abi.HostGetCurrentProcessLevel:
                    return Environment.CurrentManagedThreadId == self._audioThread ? Vst2Abi.ProcessLevelRealtime : Vst2Abi.ProcessLevelUser;
                case Vst2Abi.HostSizeWindow:
                    Interlocked.Exchange(ref self._pendingSize, ((long)index << 32) | (uint)(int)value);
                    Volatile.Write(ref self._sizePending, 1);
                    return 1;
                default:
                    return 0; // automation, begin/end edit, idle and the rest: nothing for OmniControl to do
            }
        }
        catch (Exception)
        {
            return 0; // an exception must never cross into the plug-in's native code (it would end the process)
        }
    }

    /// The instance whose handle OmniControl put into the plug-in's AEffect.user; null before that or after closing.
    private static Vst2Effect? SelfOf(Vst2Abi.AEffect* effect)
    {
        if (effect is null || effect->user == IntPtr.Zero) return null;
        return GCHandle.FromIntPtr(effect->user).Target is Vst2Effect self && self._effect == effect ? self : null;
    }

    /// audioMasterCanDo: compares the plug-in's ASCII text byte for byte (no allocation on the audio thread).
    private static bool CanDo(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return false;
        var text = (byte*)ptr;
        var names = HostCanDo;
        for (var start = 0; start < names.Length;)
        {
            var end = names[start..].IndexOf((byte)0) + start;
            var i = 0;
            while (i < end - start && text[i] == names[start + i]) i++;
            if (i == end - start && text[i] == 0) return true;
            start = end + 1;
        }
        return false;
    }

    /// Writes an ASCII host text into the plug-in's buffer without a managed array (audio-thread safe).
    private static IntPtr WriteText(IntPtr ptr, string text, int max)
    {
        if (ptr == IntPtr.Zero) return 0;
        var n = Math.Min(text.Length, max - 1);
        var bytes = (byte*)ptr;
        for (var i = 0; i < n; i++) bytes[i] = (byte)text[i];
        bytes[n] = 0;
        return 1;
    }

    /// A text the plug-in writes for a call (a name, a value's display, a label; UTF-8, as Equalizer APO reads names). UI thread.
    private string AskText(int opcode, int index)
    {
        var buffer = stackalloc byte[Vst2Abi.TextBytes];
        new Span<byte>(buffer, Vst2Abi.TextBytes).Clear();
        Dispatch(opcode, index, 0, (IntPtr)buffer, 0);
        var text = new ReadOnlySpan<byte>(buffer, Vst2Abi.TextBytes - 1); // the last byte stays 0 whatever the plug-in wrote
        var length = text.IndexOf((byte)0);
        return Encoding.UTF8.GetString(length < 0 ? text : text[..length]).Trim();
    }
}
