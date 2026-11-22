namespace OmniControl.Core.Engine;

/// One loaded VST2 effect (a 64-bit .dll) through its plain C interface (<see cref="Vst2Abi"/>; hosting ported from TabForge's
/// <c>Vst2Plugin</c>, same owner): mono or stereo in and out like the VST3 path, processReplacing (or the old accumulating process)
/// on channel buffers allocated once in native memory, so <see cref="Process"/> never allocates. It runs inside OmniControl's own
/// process: a plug-in that crashes natively takes OmniControl with it (the engine journal then restores the sound at the next start).
/// The module is never unloaded once its code ran (plug-ins crash when unloaded while their own threads still run); it stays mapped
/// until OmniControl exits. Host callback in Vst2Effect.Host.cs, settings list in Vst2Effect.Params.cs, editor in Vst2Editor.cs.
internal sealed unsafe partial class Vst2Effect : IPluginEffect
{
    /// A plug-in declaring more audio inputs or outputs than this is refused (its pointer arrays are made as declared).
    private const int MaxChannels = 128;

    /// A saved program chunk larger than this is refused (a broken plug-in must not make OmniControl copy without end).
    private const int MaxChunkBytes = 64 * 1024 * 1024;

    private readonly Vst2Abi.AEffect* _effect;
    private readonly delegate* unmanaged[Cdecl]<Vst2Abi.AEffect*, int, int, IntPtr, IntPtr, float, IntPtr> _dispatch;
    private readonly delegate* unmanaged[Cdecl]<Vst2Abi.AEffect*, float**, float**, int, void> _process;

    /// The plug-in has only the old process call, which adds into its outputs: they are cleared before each block.
    private readonly bool _accumulates;

    private readonly int _sampleRate;
    private readonly float** _inputs;
    private readonly float** _outputs;
    private readonly int _inputCount, _outputCount;
    private readonly Vst2Abi.VstTimeInfo* _time;

    /// This object, as the plug-in's <see cref="Vst2Abi.AEffect.user"/> field holds it for the host callback.
    private GCHandle _self;

    /// Held while the plug-in processes, so the UI thread's settings changes and state loads never run beside the audio thread.
    private readonly object _processGate = new();

    /// The managed id of the thread that last processed (the callback answers "real time" to it).
    private volatile int _audioThread;

    private double _samplePos;
    private bool _disposed;

    private Vst2Effect(string path, IntPtr entry, int sampleRate, int maxBlockFrames)
    {
        _sampleRate = sampleRate;
        MaxBlockFrames = maxBlockFrames;
        _time = (Vst2Abi.VstTimeInfo*)NativeMemory.AllocZeroed((nuint)sizeof(Vst2Abi.VstTimeInfo));
        _time->sampleRate = sampleRate;
        var opened = false;
        try
        {
            _effect = ((delegate* unmanaged[Cdecl]<IntPtr, Vst2Abi.AEffect*>)entry)(
                (IntPtr)(delegate* unmanaged[Cdecl]<Vst2Abi.AEffect*, int, int, IntPtr, IntPtr, float, IntPtr>)&HostCallback);
            if (_effect is null || _effect->magic != Vst2Abi.Magic || _effect->dispatcher == IntPtr.Zero)
                throw new InvalidOperationException("The plug-in did not start.");
            _self = GCHandle.Alloc(this);
            _effect->user = GCHandle.ToIntPtr(_self);
            _dispatch = (delegate* unmanaged[Cdecl]<Vst2Abi.AEffect*, int, int, IntPtr, IntPtr, float, IntPtr>)_effect->dispatcher;
            Dispatch(Vst2Abi.EffOpen, 0, 0, IntPtr.Zero, 0);
            opened = true;

            // Read after effOpen, where some plug-ins settle their inputs and outputs.
            if ((_effect->flags & Vst2Abi.FlagIsSynth) != 0 || _effect->numInputs <= 0) throw new InvalidOperationException(PluginLoader.Instrument);
            if (_effect->numOutputs <= 0) throw new InvalidOperationException("The plug-in has no audio output.");
            if (_effect->numInputs > MaxChannels || _effect->numOutputs > MaxChannels)
                throw new InvalidOperationException($"The plug-in asks for more than {MaxChannels} channels.");
            var replacing = (_effect->flags & Vst2Abi.FlagCanReplacing) != 0 && _effect->processReplacing != IntPtr.Zero;
            var process = replacing ? _effect->processReplacing : _effect->process;
            if (process == IntPtr.Zero) throw new InvalidOperationException("The plug-in can't process 32-bit sound.");
            _process = (delegate* unmanaged[Cdecl]<Vst2Abi.AEffect*, float**, float**, int, void>)process;
            _accumulates = !replacing;

            _inputCount = _effect->numInputs;
            _outputCount = _effect->numOutputs;
            _inputs = Buffers(_inputCount, maxBlockFrames);
            _outputs = Buffers(_outputCount, maxBlockFrames);
            Name = AskText(Vst2Abi.EffGetEffectName, 0) is { Length: > 0 } name ? name
                : AskText(Vst2Abi.EffGetProductString, 0) is { Length: > 0 } product ? product : Path.GetFileNameWithoutExtension(path);

            Dispatch(Vst2Abi.EffSetSampleRate, 0, 0, IntPtr.Zero, sampleRate);
            Dispatch(Vst2Abi.EffSetBlockSize, 0, maxBlockFrames, IntPtr.Zero, 0);
            Dispatch(Vst2Abi.EffMainsChanged, 0, 1, IntPtr.Zero, 0);
            Dispatch(Vst2Abi.EffStartProcess, 0, 0, IntPtr.Zero, 0);
            _defaults = ReadValues();
        }
        catch (InvalidOperationException)
        {
            Close(opened, started: false); // a refusal comes before processing was started
            throw;
        }
    }

    public string Name { get; } = string.Empty;
    public int MaxBlockFrames { get; }
    public int InputChannels => Math.Min(_inputCount, 2);
    public int OutputChannels => Math.Min(_outputCount, 2);
    public int LatencyFrames => _disposed ? 0 : Math.Max(0, _effect->initialDelay);

    private bool HasChunks => (_effect->flags & Vst2Abi.FlagProgramChunks) != 0;

    /// Loads a 64-bit VST2 .dll and sets it up for the given rate and block size (UI thread).
    public static Vst2Effect Load(string path, int sampleRate, int maxBlockFrames)
    {
        if (Vst2File.Problem(path) is { } problem) throw new InvalidOperationException(problem);
        var module = PluginLoader.LoadLibrary(path);
        if (!NativeLibrary.TryGetExport(module, Vst2File.Entry, out var entry) && !NativeLibrary.TryGetExport(module, Vst2File.OldEntry, out entry))
        {
            NativeLibrary.Free(module); // none of its code ran yet
            throw new InvalidOperationException("This DLL isn't a VST2 plug-in.");
        }
        return new Vst2Effect(path, entry, sampleRate, maxBlockFrames);
    }

    private static float** Buffers(int channels, int frames)
    {
        var list = (float**)NativeMemory.AllocZeroed((nuint)(channels * sizeof(float*)));
        for (var c = 0; c < channels; c++) list[c] = (float*)NativeMemory.AllocZeroed((nuint)(frames * sizeof(float)));
        return list;
    }

    private static void Free(float** list, int channels)
    {
        if (list is null) return;
        for (var c = 0; c < channels; c++) NativeMemory.Free(list[c]);
        NativeMemory.Free(list);
    }

    private IntPtr Dispatch(int opcode, int index, nint value, IntPtr ptr, float opt) => _dispatch(_effect, opcode, index, value, ptr, opt);

    /// Audio thread (see <see cref="IPluginEffect.Process"/>): the interleaved frames go into the plug-in's own channel buffers and back.
    public void Process(ReadOnlySpan<float> input, Span<float> output, int frames)
    {
        frames = Math.Min(frames, MaxBlockFrames);
        lock (_processGate)
        {
            if (_disposed) return;
            _audioThread = Environment.CurrentManagedThreadId;
            var ins = InputChannels;
            for (var c = 0; c < _inputCount; c++)
            {
                var channel = new Span<float>(_inputs[c], frames);
                if (c >= ins) { channel.Clear(); continue; } // side-chain and extra inputs hear silence
                for (var f = 0; f < frames; f++) channel[f] = input[f * ins + c];
            }
            if (_accumulates)
                for (var c = 0; c < _outputCount; c++) new Span<float>(_outputs[c], frames).Clear();
            _time->samplePos = _samplePos;
            _process(_effect, _inputs, _outputs, frames);
            _samplePos += frames;
            var outs = OutputChannels;
            for (var c = 0; c < outs; c++)
            {
                var channel = new ReadOnlySpan<float>(_outputs[c], frames);
                for (var f = 0; f < frames; f++) output[f * outs + c] = channel[f];
            }
        }
    }

    /// The program chunk for a plug-in with chunks, else every parameter's name and value (<see cref="Vst2Settings"/>).
    public byte[] SaveState()
    {
        if (_disposed) throw new InvalidOperationException("The plug-in is closed.");
        if (!HasChunks) return new Vst2Settings(null, Named(ReadValues())).ToBytes();
        IntPtr data;
        var size = (long)Dispatch(Vst2Abi.EffGetChunk, Vst2Abi.ProgramChunk, 0, (IntPtr)(&data), 0);
        if (size <= 0 || size > MaxChunkBytes || data == IntPtr.Zero) throw new InvalidOperationException("The plug-in didn't hand out its settings.");
        return Vst2Settings.ChunkState(new ReadOnlySpan<byte>((void*)data, (int)size));
    }

    /// Gives back settings from <see cref="SaveState"/>: the chunk, or the values in the plug-in's order.
    public void LoadState(byte[] state)
    {
        if (Vst2Settings.Read(state) is not { } settings) throw new ArgumentException("The saved settings aren't a VST2 plug-in's.", nameof(state));
        lock (_processGate)
        {
            if (_disposed) return;
            if (settings.Chunk is { Length: > 0 } chunk)
            {
                if (!HasChunks) return; // the plug-in changed how it saves (a new version): it starts from its defaults
                fixed (byte* bytes = chunk) Dispatch(Vst2Abi.EffSetChunk, Vst2Abi.ProgramChunk, chunk.Length, (IntPtr)bytes, 0);
                return;
            }
            for (var i = 0; i < Math.Min(settings.Values.Count, ParameterCount); i++) SetValue(i, settings.Values[i].Value);
        }
    }

    public IPluginEditor? CreateEditor() =>
        !_disposed && (_effect->flags & Vst2Abi.FlagHasEditor) != 0 ? new Vst2Editor(this) : null;

    public void Dispose()
    {
        lock (_processGate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        CloseEditor();
        Close(opened: true, started: true);
    }

    /// Stops (when started) and closes the plug-in and frees what this object made (the module stays loaded, see the class notes).
    private void Close(bool opened, bool started)
    {
        if (_effect is not null && started)
        {
            Dispatch(Vst2Abi.EffStopProcess, 0, 0, IntPtr.Zero, 0);
            Dispatch(Vst2Abi.EffMainsChanged, 0, 0, IntPtr.Zero, 0);
        }
        // Before effClose: the plug-in frees its AEffect there, and a late callback must not find this object.
        if (_effect is not null && _self.IsAllocated) _effect->user = IntPtr.Zero;
        if (_effect is not null && opened) Dispatch(Vst2Abi.EffClose, 0, 0, IntPtr.Zero, 0);
        if (_self.IsAllocated) _self.Free();
        Free(_inputs, _inputCount);
        Free(_outputs, _outputCount);
        NativeMemory.Free(_time);
    }
}
