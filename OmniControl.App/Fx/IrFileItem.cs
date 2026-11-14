using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Audio.Ir;
using OmniControl.App.Pages;

namespace OmniControl.App.Fx;

/// <summary>
/// The IR Loader's file card (design from TabForge's IR Loader window, MIT): the chosen file's path, Browse, dropping a file on the card,
/// and the picture of the impulse response (its waveform above its frequency response, <see cref="IrGraph"/>). The picture is worked out
/// once per file on a worker from the file itself, so it is true whichever engine plays the effect; it says nothing about how the effect is
/// processed (the FX window's status line does).
/// </summary>
public sealed partial class IrFileItem : ObservableObject, IEditorItem, IRedrawSource
{
    /// <summary>The file types the effect takes (Equalizer APO's convolution reads them); only a WAV file can be drawn.</summary>
    public static readonly string[] Extensions = { ".wav", ".flac", ".aif", ".aiff" };

    private const int PicturePoints = 600, CurvePoints = 400;
    private const string DropHint = "Drop a .wav file here, or choose one above";
    private const string NotDrawnHint = "The picture is drawn for WAV files only.";
    private const string ReadingHint = "Reading the file...";

    private readonly Action<string> _save;
    private int _generation;

    public IrFileItem(string path, Action<string> save)
    {
        _path = path;
        _save = save;
        ShowPicture();
    }

    public string Label => "Impulse response file";

    [ObservableProperty, NotifyPropertyChangedFor(nameof(PathText))] private string _path;

    /// <summary>The file's path, or "(none)".</summary>
    public string PathText => Path.Length == 0 ? "(none)" : Path;

    /// <summary>The waveform's peaks (0 to 1) and the response curve in dB below the loudest frequency; both empty while there is nothing to draw.</summary>
    public float[] Peaks { get; private set; } = Array.Empty<float>();
    public float[] Curve { get; private set; } = Array.Empty<float>();

    /// <summary>What the picture says while it has no data: the drop hint, or why the file is not drawn.</summary>
    public string Hint { get; private set; } = DropHint;

    /// <summary>The file's length, sample rate and channels once read ("1200 ms, 48000 Hz, stereo"), empty otherwise.</summary>
    [ObservableProperty] private string _facts = string.Empty;

    public event Action? Redraw;

    /// <summary>The file is of a type the effect takes.</summary>
    public bool Accepts(string path) => Extensions.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>The file a user chose or dropped (a type the effect takes only).</summary>
    public void Choose(string path)
    {
        if (!Accepts(path)) return;
        Path = path;
        _save(path);
        ShowPicture();
    }

    private void ShowPicture()
    {
        var generation = ++_generation;
        var shown = Path;
        Peaks = Curve = Array.Empty<float>();
        Facts = string.Empty;
        if (shown.Length == 0 || !shown.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            Hint = shown.Length == 0 ? DropHint : NotDrawnHint;
            Redraw?.Invoke();
            return;
        }
        Hint = ReadingHint;
        Redraw?.Invoke();
        _ = Task.Run(() =>
        {
            float[] peaks = Array.Empty<float>(), curve = Array.Empty<float>();
            string hint = string.Empty, facts = string.Empty;
            try
            {
                var audio = IrWaveFile.Read(shown);
                peaks = IrPictures.Peaks(audio.Channels[0], PicturePoints);
                curve = IrPictures.ResponseCurve(audio.Channels[0], audio.SampleRate, CurvePoints);
                facts = $"{(long)audio.Length * 1000 / audio.SampleRate} ms, {audio.SampleRate} Hz, {(audio.Channels.Length == 1 ? "mono" : "stereo")}";
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                hint = ex is FileNotFoundException ? $"The IR file is missing: {shown}" : ex.Message;
            }
            Dispatcher.UIThread.Post(() =>
            {
                if (generation != _generation) return; // another file was chosen meanwhile
                (Peaks, Curve, Hint, Facts) = (peaks, curve, hint, facts);
                Redraw?.Invoke();
            });
        });
    }
}
