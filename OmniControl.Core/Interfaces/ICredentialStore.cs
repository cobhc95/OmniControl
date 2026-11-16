namespace OmniControl.Core.Interfaces;

/// <summary>Keeps one secret (a password) under a name, outside every OmniControl file.</summary>
public interface ICredentialStore
{
    /// <summary>Saves the secret; false when Windows refused.</summary>
    bool Write(string target, string secret);

    /// <summary>The saved secret, or null when there is none.</summary>
    string? Read(string target);

    /// <summary>Removes the secret; a missing one is not a problem.</summary>
    void Delete(string target);
}
