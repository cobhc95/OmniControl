using NAudio.Vst3;

namespace OmniControl.Core.Engine;

/// One loaded VST3 effect (through NAudio.Vst3; mono or stereo in and out, see <see cref="InputChannels"/>). It runs inside
/// OmniControl's own process: a plug-in that crashes natively takes OmniControl with it (the engine journal then restores the
/// sound at the next start). Every load failure is thrown as <see cref="InvalidOperationException"/> with one plain sentence.
/// Its settings for a host's own list are in Vst3Effect.Params.cs.
internal sealed partial class Vst3Effect : IPluginEffect
{
    private readonly Vst3Module _module;
    private readonly Vst3Plugin _plugin;

    /// Held while the plug-in processes, so a flush from the UI thread (<see cref="FlushIfIdle"/>) never runs beside the audio thread.
    private readonly object _processGate = new();

    /// When the audio thread last ran the plug-in (Environment.TickCount64; 0 = never).
    private long _lastProcessTicks;

    private Vst3Effect(Vst3Module module, Vst3Plugin plugin, string name)
    {
        _module = module;
        _plugin = plugin;
        Name = name;
    }

    public string Name { get; }
    public int MaxBlockFrames => _plugin.MaxBlockSize;

    public int InputChannels => _plugin.InputChannelCount;
    public int OutputChannels => _plugin.OutputChannelCount;
    public int LatencyFrames => (int)Math.Min(_plugin.LatencySamples, int.MaxValue);

    /// Loads the first effect class of a .vst3 file or bundle and sets it up for the given rate and block size.
    internal static Vst3Effect Load(string path, int sampleRate, int maxBlockFrames)
    {
        if (Vst3File.Problem(path) is { } problem) throw new InvalidOperationException(problem);
        var module = Open(() => Vst3Module.Load(path));
        try
        {
            var classes = Open(module.GetClasses);
            var effect = classes.FirstOrDefault(c => c.IsEffect) ?? throw new InvalidOperationException(
                classes.Any(c => c.IsInstrument) ? PluginLoader.Instrument : "The file has no audio effect in it.");
            return new Vst3Effect(module, Open(() => module.CreatePlugin(effect, sampleRate, maxBlockFrames)), effect.Name);
        }
        catch (InvalidOperationException)
        {
            module.Dispose();
            throw;
        }
    }

    /// Runs one loading step and turns what can go wrong into one plain sentence (the detail stays as the inner exception).
    private static T Open<T>(Func<T> step)
    {
        try
        {
            return step();
        }
        catch (Exception ex) when (AppControl.IsBlock(ex))
        {
            throw new InvalidOperationException(PluginLoader.Blocked, ex);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException or IOException
                                       or COMException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException($"Couldn't load the plug-in: {ex.Message}", ex);
        }
    }

    /// Audio thread (see <see cref="IPluginEffect.Process"/>).
    public void Process(ReadOnlySpan<float> input, Span<float> output, int frames)
    {
        lock (_processGate) _plugin.Process(input, output, frames);
        Volatile.Write(ref _lastProcessTicks, Environment.TickCount64);
    }

    /// The plug-in's full settings, to store with the chain (setting changes not yet processed are handed over first).
    public byte[] SaveState()
    {
        FlushIfIdle();
        return _plugin.SaveState();
    }

    public void LoadState(byte[] state) => _plugin.LoadState(state);

    /// The plug-in's own editor, or null when it has none. Call on the UI thread; dispose the editor before this effect.
    public IPluginEditor? CreateEditor() => _plugin.CreateView() is { } view ? new Vst3Editor(view) : null;

    public void Dispose()
    {
        _plugin.Dispose();
        _module.Dispose();
    }
}

/// A VST3 plug-in's editor (IPlugView through NAudio.Vst3); see <see cref="IPluginEditor"/>.
internal sealed class Vst3Editor : IPluginEditor
{
    /// Two content scales closer than this are the same (float rounding).
    private const double ScaleTolerance = 0.001;

    private readonly Vst3PluginView _view;
    private IntPtr _parent;
    private bool _disposed;

    /// The content scale the editor last accepted (display scale times the user's editor scale).
    private double _scale = 1;

    /// The plug-in asked for a new size during the call in <see cref="TryScale"/>.
    private bool _resizedNow;

    internal Vst3Editor(Vst3PluginView view)
    {
        _view = view;
        _view.Resized += OnResized;
    }

    /// The editor asked for a new size (pixels; IPlugFrame::resizeView). It takes that size once the handler returns.
    public event Action<int, int>? Resized;

    public (int Width, int Height) Size
    {
        get
        {
            if (_disposed) return (0, 0);
            var size = _view.GetSize();
            return (size.Width, size.Height);
        }
    }

    /// False once the editor is disposed (its owner closed it).
    public bool IsOpen => !_disposed;

    /// The plug-in lets the host change its editor's size (IPlugView::canResize).
    public bool CanResize => !_disposed && PluginCall.Ask(_view.CanResize, false);

    /// Shows the editor inside <paramref name="parentWindow"/>; throws <see cref="NotSupportedException"/> when it can't.
    public void AttachTo(IntPtr parentWindow, double scale)
    {
        if (_disposed) return;
        _view.AttachTo(parentWindow, (float)scale);
        _parent = parentWindow;
        _scale = scale;
    }

    /// Takes the editor out of its parent window (IPlugView::removed). Call before that window is destroyed.
    public void Detach()
    {
        if (_disposed) return;
        _view.Detach();
        _parent = IntPtr.Zero;
    }

    /// Offers a resizable editor a new size: the plug-in first clamps it to one it can show (checkSizeConstraint), then is told
    /// (onSize). Returns the size it took; a fixed-size editor, or a plug-in that refuses, keeps its size.
    public (int Width, int Height) Offer(int width, int height)
    {
        if (!CanResize) return Size;
        return PluginCall.Ask(() =>
        {
            var fit = _view.CheckSizeConstraint(width, height);
            var now = _view.GetSize();
            if (fit.Width != now.Width || fit.Height != now.Height) _view.SetSize(fit.Width, fit.Height);
            return (fit.Width, fit.Height);
        }, Size);
    }

    /// Asks the editor to draw at another content scale (IPlugViewContentScaleSupport::setContentScaleFactor; 1.0 = 96 DPI at 100 %):
    /// the display's scale times the user's editor scale. True when the editor took it, which a host can only see from the editor
    /// changing its size (asking for a new size, or reporting one). A plug-in that ignores the call is put back to the scale it had,
    /// and false says it can't be scaled by the host.
    public bool TryScale(double scale)
    {
        if (_disposed) return false;
        if (Math.Abs(scale - _scale) < ScaleTolerance) return true;
        var before = Size;
        _resizedNow = false;
        PluginCall.Ask(() => { _view.SetContentScaleFactor((float)scale); return true; }, false);
        if (_resizedNow || Size != before)
        {
            _scale = scale;
            return true;
        }
        PluginCall.Ask(() => { _view.SetContentScaleFactor((float)_scale); return true; }, false);
        return false;
    }

    /// Moves the plug-in's own window inside the parent window (see <see cref="PluginWindow.MoveFirstChild"/>).
    public void MoveView(int x, int y)
    {
        if (!_disposed) PluginWindow.MoveFirstChild(_parent, x, y);
    }

    /// VST3 editors run on the host's message loop and report sizes on the UI thread: nothing to do.
    public void Idle() { }

    private void OnResized(object? sender, Vst3ViewSize size)
    {
        _resizedNow = true;
        Resized?.Invoke(size.Width, size.Height);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _view.Resized -= OnResized;
        _view.Detach();
        _view.Dispose();
        _disposed = true;
    }
}
