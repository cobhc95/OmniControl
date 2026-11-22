namespace OmniControl.Core.Engine;

/// The parts of the VST 2.4 plug-in interface (the plain C ABI a VST2 .dll exposes) that OmniControl uses, as OmniControl's own
/// definitions: taken from TabForge (Vst2Plugin.cs, same owner, MIT), which match the public clean-room descriptions of the interface
/// (VeSTige-style headers). No file of Steinberg's discontinued VST2 SDK is used or copied. Numbers are fixed by the ABI.
internal static class Vst2Abi
{
    /// 'VstP', the first field of every AEffect.
    public const int Magic = 0x56737450;

    /// The host's answer to audioMasterVersion: VST 2.4.
    public const int HostVersionNumber = 2400;

    /// A name or text the plug-in writes; real plug-ins write more than the interface's own short limits, so the buffer is generous.
    public const int TextBytes = 256;

    /// The program chunk (effGetChunk / effSetChunk index 1): one program's settings, which Equalizer APO also applies.
    public const int ProgramChunk = 1;

    // Plug-in flags (AEffect.flags).
    public const int FlagHasEditor = 1 << 0, FlagCanReplacing = 1 << 4, FlagProgramChunks = 1 << 5, FlagIsSynth = 1 << 8;

    // Calls into the plug-in (dispatcher opcodes).
    public const int EffOpen = 0, EffClose = 1, EffGetParamLabel = 6, EffGetParamDisplay = 7, EffGetParamName = 8,
        EffSetSampleRate = 10, EffSetBlockSize = 11, EffMainsChanged = 12, EffEditGetRect = 13, EffEditOpen = 14, EffEditClose = 15,
        EffEditIdle = 19, EffGetChunk = 23, EffSetChunk = 24, EffGetEffectName = 45, EffGetProductString = 48,
        EffStartProcess = 71, EffStopProcess = 72;

    // Calls from the plug-in into the host (audioMaster opcodes) that OmniControl answers; every other one is answered 0.
    public const int HostVersion = 1, HostCurrentId = 2, HostGetTime = 7, HostIoChanged = 13, HostSizeWindow = 15, HostGetSampleRate = 16,
        HostGetBlockSize = 17, HostGetCurrentProcessLevel = 23, HostGetVendorString = 32, HostGetProductString = 33, HostGetVendorVersion = 34,
        HostCanDo = 37, HostGetLanguage = 38, HostUpdateDisplay = 42;

    /// Calls that say the plug-in's settings changed (audioMasterAutomate, audioMasterEndEdit; also audioMasterUpdateDisplay above).
    public const int HostAutomate = 0, HostEndEdit = 44;

    /// audioMasterGetCurrentProcessLevel answers: called from the UI thread, from the audio thread.
    public const int ProcessLevelUser = 1, ProcessLevelRealtime = 2;

    /// audioMasterGetLanguage: English (0 means none, and some plug-ins index a table with it).
    public const int LanguageEnglish = 1;

    /// The plug-in's description of itself (VST 2.4 AEffect, 64-bit layout).
    [StructLayout(LayoutKind.Sequential)]
    public struct AEffect
    {
        public int magic;
        public IntPtr dispatcher, process, setParameter, getParameter;
        public int numPrograms, numParams, numInputs, numOutputs, flags;
        public IntPtr resvd1, resvd2;
        public int initialDelay, realQualities, offQualities;
        public float ioRatio;
        /// The plug-in's own object; never touched.
        public IntPtr obj;
        /// Left to the host: OmniControl keeps a handle to its <see cref="Vst2Effect"/> here, so the callback finds it.
        public IntPtr user;
        public int uniqueID, version;
        public IntPtr processReplacing, processDoubleReplacing;
    }

    /// An editor's rectangle (effEditGetRect), in pixels.
    [StructLayout(LayoutKind.Sequential)]
    public struct ERect
    {
        public short top, left, bottom, right;
    }

    /// What audioMasterGetTime hands out. OmniControl plays a live stream: only the rate and the sample position are valid.
    [StructLayout(LayoutKind.Sequential)]
    public struct VstTimeInfo
    {
        public double samplePos, sampleRate, nanoSeconds, ppqPos, tempo, barStartPos, cycleStartPos, cycleEndPos;
        public int timeSigNumerator, timeSigDenominator, smpteOffset, smpteFrameRate, samplesToNextClock, flags;
    }
}
