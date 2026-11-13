using System.Security.Cryptography;
using System.Text;

namespace OmniControl.App.Rules;

/// <summary>
/// The one rule for allowed commands: a rule step that runs a command or opens a program is identified by a SHA-256 of its whole
/// stored text (command, shell, arguments, folder...), so changing any part of it needs a new approval.
/// </summary>
public static class CommandApproval
{
    public static string HashOf(string storedStep) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(storedStep)));
}
