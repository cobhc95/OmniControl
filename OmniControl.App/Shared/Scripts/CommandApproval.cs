using System.Security.Cryptography;
using System.Text;

namespace OmniControl.App.Shared.Scripts;

/// The one rule for allowed commands: a rule step that runs a command or opens a program is identified by a SHA-256 of its whole
/// stored text (command, shell, arguments, folder...), so changing any part of it needs a new approval.
public static class CommandApproval
{
    public static string HashOf(string storedStep) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storedStep)));

    /// The step is in a store's approved set. Locked: a runner asks on a worker thread while an editor may approve on the UI thread.
    public static bool IsIn(HashSet<string> approved, string storedStep)
    {
        var hash = HashOf(storedStep);
        lock (approved) return approved.Contains(hash);
    }

    /// Adds the step to a store's approved set (the owner saves it).
    public static void AddTo(HashSet<string> approved, string storedStep)
    {
        var hash = HashOf(storedStep);
        lock (approved) approved.Add(hash);
    }
}
