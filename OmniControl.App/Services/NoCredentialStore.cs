using OmniControl.Core.Interfaces;

namespace OmniControl.App.Services;

/// <summary>A credential store with nothing in it that keeps nothing: what an inert run (a screenshot export) is given, so no stored password is ever read.</summary>
public sealed class NoCredentialStore : ICredentialStore
{
    public bool Write(string target, string secret) => false;

    public string? Read(string target) => null;

    public void Delete(string target) { }
}
