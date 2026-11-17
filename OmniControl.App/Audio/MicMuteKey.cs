using OmniControl.App.Services;
using OmniControl.Core.Engine.Keyboard;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.App.Audio;

/// <summary>What is kept of the learned microphone mute key (<c>mic-mute-key.json</c>).</summary>
public sealed class MicMuteKeySettings
{
    /// <summary>The microphone mute sends the learned key instead of changing Windows' mute.</summary>
    public bool Enabled { get; set; }

    public ushort Vk { get; set; }
    public ushort Scan { get; set; }
    public bool Extended { get; set; }

    public bool HasKey => Vk != 0 || Scan != 0;

    public static MicMuteKeySettings Load() => AppDataStore.Load<MicMuteKeySettings>(DataStores.MicMuteKey) ?? new();

    public void Save() => AppDataStore.Save(DataStores.MicMuteKey, this);
}

/// <summary>
/// "Mute the way your keyboard does": many laptops light their microphone-mute LED only when their own key is pressed, because the maker's
/// software handles that key. The user presses their key once (<see cref="SetEnabled"/> starts listening, <see cref="KeyLearner"/>), it is
/// kept, and the microphone mute can then send exactly that key (<see cref="TrySend"/>, rate limited, always released) so the maker's
/// software does the muting. It never checks the result: the owner of the mute reads it again and falls back to Windows' own mute.
/// </summary>
public sealed class MicMuteKey
{
    public const string CannotCopyWords = "This keyboard's mute key can't be copied; the normal mute is used.";
    private const string LearningWords = "Press your microphone mute key now", LearnedWords = "Microphone mute key learned.";

    private readonly MicMuteKeySettings _settings = MicMuteKeySettings.Load();
    private readonly LaunchLimiter _limiter = new(Timing.MicKeyBurstMax, Timing.MicKeyBurstWindow);
    private readonly Action<string> _report;
    private int _learning;

    /// <param name="report">The one status path (ShellViewModel.Report; thread-safe).</param>
    public MicMuteKey(Action<string> report) => _report = report;

    public bool IsLearning => Volatile.Read(ref _learning) != 0;

    /// <summary>The switch as the settings pane shows it: on once a key is learned and in use, and while the key is being waited for.</summary>
    public bool IsOn => _settings.Enabled || IsLearning;

    /// <summary>True when the learned key is in use.</summary>
    public bool Active => _settings.Enabled && _settings.HasKey;

    /// <summary>What to tell beside the switch while it waits for the key; null otherwise.</summary>
    public string? Status => IsLearning ? LearningWords : null;

    /// <summary>Turns the key on (learning it first when none is kept yet) or off.</summary>
    public void SetEnabled(bool on)
    {
        if (!on || _settings.HasKey)
        {
            _settings.Enabled = on;
            _settings.Save();
            return;
        }
        _ = Guard.RunAsync("Learning the microphone mute key", LearnAsync);
    }

    private async Task LearnAsync()
    {
        if (Interlocked.Exchange(ref _learning, 1) != 0) return;
        try
        {
            if (await KeyLearner.LearnAsync(Timing.MicKeyLearn, Timing.MicKeyLearnSettle) is not { } key)
            {
                _report(CannotCopyWords);
                return;
            }
            (_settings.Vk, _settings.Scan, _settings.Extended, _settings.Enabled) = (key.Vk, key.Scan, key.Extended, true);
            _settings.Save();
            _report(LearnedWords);
        }
        finally
        {
            Volatile.Write(ref _learning, 0);
        }
    }

    /// <summary>Sends the learned key (press and release together). False when none is in use, the rate limit says too often, or Windows refused it.</summary>
    public bool TrySend() => Active && _limiter.TryAcquire() && InputSender.Tap(_settings.Vk, _settings.Scan, _settings.Extended);
}
