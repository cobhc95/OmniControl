namespace OmniControl.Core.Engine;

/// One setting of a plug-in that a host may show in its own list. <paramref name="Id"/> is the plug-in's id (VST3) or its index (VST2).
/// <paramref name="StepCount"/>: 0 = continuous, 1 = on/off, more = that many steps above the first. Values are normalised (0..1).
/// <paramref name="Group"/> is the plug-in's unit the setting belongs to ("Band 1", "Filter / Envelope"); empty for the plug-in as a whole.
/// <paramref name="Units"/> is added to the plug-in's value text when that text does not end with it already.
public sealed record PluginParam(uint Id, string Title, string Units, int StepCount, double DefaultValue, string Group);

/// One loaded plug-in effect, whatever its format (VST3, VST2), as OmniControl's chain, editors and settings list use it. It runs
/// inside OmniControl's own process. Load and every call except <see cref="Process"/> on the UI thread (the thread that loaded it).
public interface IPluginEffect : IDisposable
{
    string Name { get; }

    /// Most frames one <see cref="Process"/> call may take.
    int MaxBlockFrames { get; }

    /// Channels per input frame of <see cref="Process"/> (1 or 2).
    int InputChannels { get; }

    /// Channels per output frame of <see cref="Process"/> (1 or 2).
    int OutputChannels { get; }

    /// How many frames late the plug-in's output is, as it reports it (it may change while it runs).
    int LatencyFrames { get; }

    /// Audio thread, allocation-free. Buffers are interleaved with <see cref="InputChannels"/> / <see cref="OutputChannels"/> floats per
    /// frame; <paramref name="frames"/> must not exceed <see cref="MaxBlockFrames"/>.
    void Process(ReadOnlySpan<float> input, Span<float> output, int frames);

    /// The plug-in's full settings, to store with the chain; throws <see cref="InvalidOperationException"/> when it refuses.
    byte[] SaveState();

    /// True when the settings may have changed since the last call: after a change notice from a plug-in known to send them, or
    /// always for one that has sent none (the host then reads and compares).
    bool TakeChanged();

    /// Gives back settings from <see cref="SaveState"/>; throws <see cref="InvalidOperationException"/> or <see cref="ArgumentException"/> when they are rejected.
    void LoadState(byte[] state);

    /// The plug-in's own editor, or null when it has none. Dispose the editor before this effect.
    IPluginEditor? CreateEditor();

    /// The settings a user may change, in the plug-in's order (read once).
    IReadOnlyList<PluginParam> Parameters { get; }

    /// The setting's current value (normalised).
    double ValueOf(uint id);

    /// Changes a setting (normalised); the sound follows from the next block.
    void Set(uint id, double value);

    /// The value as the plug-in's own editor would write it ("8.4 kHz", "Lowpass"); empty when the plug-in gives no text.
    string TextOf(uint id, double value);

    /// The current value in the setting's own units, for typing a number (a format without units: percent of the range).
    double PlainOf(uint id);

    /// Changes a setting by a value in its own units (<see cref="PlainOf"/>); a value past either end is held at that end.
    void SetPlain(uint id, double plain);
}

/// A plug-in's editor, shown inside a native child window that the app provides (UI thread only). Sizes are physical pixels. Every
/// member is safe after <see cref="IDisposable.Dispose"/> (it does nothing then), because the app's host window may go after the editor.
public interface IPluginEditor : IDisposable
{
    /// The editor asked for a new size (pixels). It takes that size once the handler returns.
    event Action<int, int>? Resized;

    (int Width, int Height) Size { get; }

    /// False once the editor is disposed (its owner closed it).
    bool IsOpen { get; }

    /// The plug-in lets the host change its editor's size.
    bool CanResize { get; }

    /// Shows the editor inside <paramref name="parentWindow"/>; throws <see cref="NotSupportedException"/> or <see cref="InvalidOperationException"/> when it can't.
    void AttachTo(IntPtr parentWindow, double scale);

    /// Takes the editor out of its parent window. Call before that window is destroyed.
    void Detach();

    /// Offers a resizable editor a new size; returns the size it took (a fixed-size editor keeps its size).
    (int Width, int Height) Offer(int width, int height);

    /// Asks the editor to draw at another content scale (1.0 = 96 DPI at 100 %); true when it took it.
    bool TryScale(double scale);

    /// Moves the plug-in's own window inside the parent window (pixels; zero or negative) to scroll a larger editor into view.
    void MoveView(int x, int y);

    /// Called on a UI timer while the editor is shown (<c>Timing.PluginEditorIdle</c>): a format whose editors need the host's idle
    /// calls gets them here, and a size the plug-in asked for from another thread is raised as <see cref="Resized"/> here.
    void Idle();
}
