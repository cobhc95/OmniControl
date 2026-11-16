using System.Runtime.InteropServices;
using System.Text;
using OmniControl.Core.Interfaces;
using OmniControl.Core.Native;

namespace OmniControl.Core.Engine;

/// <summary>
/// <see cref="ICredentialStore"/> in the Windows Credential Manager (the current user's, kept on this PC only): a password lives there and
/// nowhere else, never in a json file, a backup or a log.
/// </summary>
public sealed class WindowsCredentialStore : ICredentialStore
{
    // The Credential Manager shows this as the saved entry's user name; the secret is the blob.
    private const string EntryUser = "OmniControl";

    public bool Write(string target, string secret)
    {
        var bytes = Encoding.Unicode.GetBytes(secret);
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var credential = new CredentialNative.Credential
            {
                Type = CredentialNative.TypeGeneric,
                TargetName = target,
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredentialNative.PersistLocalMachine,
                UserName = EntryUser,
            };
            return CredentialNative.CredWrite(ref credential, 0);
        }
        finally
        {
            Array.Clear(bytes);
            Marshal.FreeHGlobal(blob);
        }
    }

    public string? Read(string target)
    {
        if (!CredentialNative.CredRead(target, CredentialNative.TypeGeneric, 0, out var buffer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<CredentialNative.Credential>(buffer);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0) return null;
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            var text = Encoding.Unicode.GetString(bytes);
            Array.Clear(bytes);
            return text;
        }
        finally
        {
            CredentialNative.CredFree(buffer);
        }
    }

    public void Delete(string target) => CredentialNative.CredDelete(target, CredentialNative.TypeGeneric, 0); // not found means it is already gone
}
