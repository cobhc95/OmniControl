using OmniControl.App.Services;

namespace OmniControl.App.Dialogs;

/// <summary>What is saved: remembered prompt key -> the name Settings lists it by.</summary>
public sealed class ConfirmationData
{
    public Dictionary<string, string> Remembered { get; set; } = new();
}

/// <summary>
/// The prompts the user ticked "Don't ask again" on. Only a confirmed answer is remembered (Cancel never is), only for a message that
/// <see cref="DialogMessage.CanRemember"/>, and every one can be put back (Reset) in App settings.
/// </summary>
public sealed class ConfirmationMemory
{
    private readonly Dictionary<string, string> _remembered;
    private readonly Action<ConfirmationData> _save;

    /// <summary>The remembered prompts, from and into the Confirmations store.</summary>
    public ConfirmationMemory() : this(AppDataStore.Load<ConfirmationData>(DataStores.Confirmations) ?? new(), data => AppDataStore.Save(DataStores.Confirmations, data)) { }

    public ConfirmationMemory(ConfirmationData data, Action<ConfirmationData> save)
    {
        _remembered = new Dictionary<string, string>(data.Remembered);
        _save = save;
    }

    /// <summary>Something was remembered or reset.</summary>
    public event Action? Changed;

    /// <summary>Key and name of every remembered prompt, by name.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Items => _remembered.OrderBy(p => p.Value, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>True when the user asked not to be asked about <paramref name="message"/> again (it is then taken as confirmed).</summary>
    public bool Skips(DialogMessage message) => message.CanRemember && _remembered.ContainsKey(message.RememberKey!);

    /// <summary>Remembers <paramref name="message"/> when the user confirmed it with the box ticked; anything else changes nothing.</summary>
    public void Settle(DialogMessage message, DialogAnswer answer, bool dontAskAgain)
    {
        if (!dontAskAgain || answer != DialogAnswer.Confirm || !message.CanRemember) return;
        _remembered[message.RememberKey!] = message.RememberName ?? message.RememberKey!;
        Persist();
    }

    public void Reset(string key)
    {
        if (_remembered.Remove(key)) Persist();
    }

    public void ResetAll()
    {
        if (_remembered.Count == 0) return;
        _remembered.Clear();
        Persist();
    }

    private void Persist()
    {
        _save(new ConfirmationData { Remembered = new Dictionary<string, string>(_remembered) });
        Changed?.Invoke();
    }
}
