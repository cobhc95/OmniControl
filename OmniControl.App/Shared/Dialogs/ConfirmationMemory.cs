namespace OmniControl.App.Shared.Dialogs;

/// What is saved: remembered prompt key -> the name Settings lists it by.
public sealed class ConfirmationData
{
    public Dictionary<string, string> Remembered { get; set; } = new();
}

/// The prompts the user ticked "Don't ask again" on. Only a confirmed answer is remembered (Cancel never is), only for a message that
/// <see cref="DialogMessage.CanRemember"/>, and every one can be put back (Reset) in App settings.
public sealed class ConfirmationMemory
{
    private readonly Dictionary<string, string> _remembered;
    private readonly Action<ConfirmationData> _save;

    /// The remembered prompts, from and into the Confirmations store.
    public ConfirmationMemory() : this(AppDataStore.Load<ConfirmationData>(DataStores.Confirmations) ?? new(), data => AppDataStore.Save(DataStores.Confirmations, data)) { }

    public ConfirmationMemory(ConfirmationData data, Action<ConfirmationData> save)
    {
        _remembered = new Dictionary<string, string>(data.Remembered);
        _save = save;
    }

    /// Something was remembered or reset.
    public event Action? Changed;

    /// Key and name of every remembered prompt, by name.
    public IReadOnlyList<KeyValuePair<string, string>> Items => _remembered.OrderBy(p => p.Value, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// True when the user asked not to be asked about <paramref name="message"/> again (it is then taken as confirmed).
    public bool Skips(DialogMessage message) => message.CanRemember && _remembered.ContainsKey(message.RememberKey!);

    /// Remembers <paramref name="message"/> when the user confirmed it with the box ticked; anything else changes nothing.
    public void Settle(DialogMessage message, DialogAnswer answer, bool dontAskAgain)
    {
        if (!dontAskAgain || answer != DialogAnswer.Confirm || !message.CanRemember) return;
        _remembered[message.RememberKey!] = message.RememberName ?? message.RememberKey!;
        Persist();
    }

    /// Reads the Confirmations store again (an import replaced the file); a missing or damaged file means nothing is remembered. Always true.
    public bool Reload()
    {
        _remembered.Clear();
        foreach (var (key, name) in (AppDataStore.Load<ConfirmationData>(DataStores.Confirmations) ?? new()).Remembered) _remembered[key] = name;
        Changed?.Invoke();
        return true;
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
