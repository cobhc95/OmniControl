using System.Security.Cryptography;
using System.Text;

namespace OmniControl.App.Shared.Scripts;

/// THE allowed commands of one store: whatever starts something (a rule step that runs a command or opens a program, a custom action, a
/// script) is identified by a SHA-256 of its whole stored text (command, shell, arguments, folder, script text...), so changing any part
/// of it needs a new approval. Approvals are this PC's own: a settings import never brings them (<see cref="DataStore.NotImported"/>).
public sealed class CommandApprovals
{
    private readonly Func<HashSet<string>> _approved;
    private readonly Action _save;
    private readonly Func<DialogMessage, Task<bool>> _ask;

    /// <param name="approved">The store's set of approved hashes (read each time: a reload replaces it).</param>
    /// <param name="save">Saves the store after an approval.</param>
    /// <param name="ask">Asks the person at this PC (UI thread).</param>
    public CommandApprovals(Func<HashSet<string>> approved, Action save, Func<DialogMessage, Task<bool>> ask) => (_approved, _save, _ask) = (approved, save, ask);

    private static string HashOf(string stored) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stored)));

    /// The step is approved. Locked: a runner asks on a worker thread while an editor may approve on the UI thread.
    public bool IsApproved(string stored)
    {
        var hash = HashOf(stored);
        var approved = _approved();
        lock (approved) return approved.Contains(hash);
    }

    /// True when <paramref name="stored"/> is approved already or the person allows it now (UI thread); an approval is saved at once.
    public async Task<bool> AskAsync(string stored, DialogMessage question)
    {
        if (IsApproved(stored)) return true;
        if (!await _ask(question)) return false;
        var hash = HashOf(stored);
        var approved = _approved();
        lock (approved) approved.Add(hash);
        _save();
        return true;
    }
}
