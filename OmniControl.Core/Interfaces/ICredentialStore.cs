namespace OmniControl.Core.Interfaces;

/// Keeps one secret (a password) under a name, outside every OmniControl file.
public interface ICredentialStore
{
    /// Saves the secret; false when Windows refused.
    bool Write(string target, string secret);

    /// The saved secret, or null when there is none.
    string? Read(string target);

    /// Removes the secret; a missing one is not a problem.
    void Delete(string target);
}
