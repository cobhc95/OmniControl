using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace OmniControl.Core.Engine.Apo;

/// The registry calls Equalizer APO's Device Selector makes (its RegistryHelper), on keys under HKLM (64-bit view), so the ported device
/// logic (<see cref="DeviceApo"/>) runs unchanged against Windows or, in the headless tests, against a dictionary.
internal interface IApoRegistry
{
    bool KeyExists(string key);
    bool ValueExists(string key, string name);
    string? ReadString(string key, string name);
    uint? ReadDword(string key, string name);
    void WriteString(string key, string name, string value);
    /// A REG_MULTI_SZ with one string (the processing modes).
    void WriteMulti(string key, string name, string value);
    void DeleteValue(string key, string name);
    void CreateKey(string key);
    /// Deletes an empty key (RegDeleteKeyEx).
    void DeleteKey(string key);
    bool KeyEmpty(string key);
    IReadOnlyList<string> SubKeys(string key);
    /// RegistryHelper::takeOwnership: Administrators become the key's owner (kept, as Device Selector keeps it).
    void TakeOwnership(string key);
    /// RegistryHelper::makeWritable: Administrators get full access to the key and what is made under it (kept).
    void MakeWritable(string key);
}

/// <see cref="IApoRegistry"/> on Windows' registry. Writes need administrator rights (the elevated helper, <see cref="ApoSetup"/>). Each key
/// is opened with only the access its call needs: MMDevices keys give Administrators SetValue and ReadKey only, so .NET's writable open
/// (KEY_WRITE, which adds CreateSubKey) is refused there with "Requested registry access is not allowed".
internal sealed class MachineRegistry : IApoRegistry
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private const int OwnerInfo = 1, DaclInfo = 4;

    // RegSetKeySecurity changes only the key itself; SetAccessControl's SetSecurityInfo would also rewrite every key under it.
    [DllImport("advapi32.dll")] private static extern int RegSetKeySecurity(SafeRegistryHandle key, int information, byte[] descriptor);

    private static RegistryKey Machine() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    /// The key opened with exactly <paramref name="rights"/>, or null when it is missing.
    private static RegistryKey? Open(RegistryKey machine, string key, RegistryRights rights) =>
        machine.OpenSubKey(key, RegistryKeyPermissionCheck.ReadWriteSubTree, rights);

    /// Runs a registry call; a refusal names the call and the key, so the setup log says where it stopped.
    private static void At(string doing, string key, Action call)
    {
        try
        {
            call();
        }
        catch (Exception ex) when (IsRegistryFailure(ex))
        {
            throw new IOException($"{doing} HKLM\\{key}: {ex.Message}", ex);
        }
    }

    public bool KeyExists(string key)
    {
        using var machine = Machine();
        using var k = machine.OpenSubKey(key);
        return k is not null;
    }

    public bool ValueExists(string key, string name)
    {
        using var machine = Machine();
        using var k = machine.OpenSubKey(key);
        return k?.GetValue(name) is not null;
    }

    /// RegistryHelper::readValue: a REG_SZ; a value of another type is refused as it refuses it (null when the value is missing).
    public string? ReadString(string key, string name)
    {
        using var machine = Machine();
        using var k = machine.OpenSubKey(key);
        if (k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) is not { } value) return null;
        return k.GetValueKind(name) == RegistryValueKind.String ? (string)value
            : throw new InvalidDataException($"Registry value HKLM\\{key}\\{name} has wrong type");
    }

    public uint? ReadDword(string key, string name)
    {
        using var machine = Machine();
        using var k = machine.OpenSubKey(key);
        return k?.GetValue(name) is int v ? unchecked((uint)v) : null;
    }

    public void WriteString(string key, string name, string value) => Write(key, name, value, RegistryValueKind.String);

    public void WriteMulti(string key, string name, string value) => Write(key, name, new[] { value }, RegistryValueKind.MultiString);

    private static void Write(string key, string name, object value, RegistryValueKind kind) => At($"Couldn't write {name} in", key, () =>
    {
        using var machine = Machine();
        using var k = Open(machine, key, RegistryRights.SetValue) ?? machine.CreateSubKey(key, writable: true);
        k.SetValue(name, value, kind);
    });

    public void DeleteValue(string key, string name) => At($"Couldn't delete {name} in", key, () =>
    {
        using var machine = Machine();
        using var k = Open(machine, key, RegistryRights.SetValue);
        k?.DeleteValue(name, throwOnMissingValue: false);
    });

    public void CreateKey(string key) => At("Couldn't create", key, () =>
    {
        using var machine = Machine();
        using var k = machine.CreateSubKey(key, RegistryKeyPermissionCheck.ReadSubTree); // an existing key is only read
    });

    public void DeleteKey(string key) => At("Couldn't delete", key, () =>
    {
        using var machine = Machine();
        machine.DeleteSubKey(key, throwOnMissingSubKey: false);
    });

    public bool KeyEmpty(string key)
    {
        using var machine = Machine();
        using var k = machine.OpenSubKey(key);
        return k is null || k.SubKeyCount == 0 && k.ValueCount == 0;
    }

    public IReadOnlyList<string> SubKeys(string key)
    {
        using var machine = Machine();
        using var k = machine.OpenSubKey(key);
        return k?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    public void TakeOwnership(string key) => At("Couldn't take ownership of", key, () =>
    {
        Privileges.Enable(Privileges.TakeOwnership);
        try
        {
            using var machine = Machine();
            using var k = Open(machine, key, RegistryRights.TakeOwnership) ?? throw new IOException("The key is missing.");
            var security = new RegistrySecurity();
            security.SetOwner(Administrators);
            SetSecurity(k, OwnerInfo, security.GetSecurityDescriptorBinaryForm());
        }
        finally
        {
            Privileges.Enable(Privileges.TakeOwnership, on: false);
        }
    });

    // SetEntriesInAcl with SET_ACCESS: Administrators' own entries are replaced by one full-access entry inherited by everything under the key.
    public void MakeWritable(string key) => At("Couldn't give administrators full access to", key, () =>
    {
        using var machine = Machine();
        using var k = Open(machine, key, RegistryRights.ReadPermissions | RegistryRights.ChangePermissions) ?? throw new IOException("The key is missing.");
        var security = k.GetAccessControl(AccessControlSections.Access);
        security.SetAccessRule(new RegistryAccessRule(Administrators, RegistryRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        SetSecurity(k, DaclInfo, security.GetSecurityDescriptorBinaryForm());
    });

    private static void SetSecurity(RegistryKey key, int information, byte[] descriptor)
    {
        if (RegSetKeySecurity(key.Handle, information, descriptor) is var error and not 0) throw new IOException(new Win32Exception(error).Message);
    }

    /// The failures a registry call reports (missing rights, a key in use); everything else is a bug and is not caught.
    public static bool IsRegistryFailure(Exception ex) => ex is UnauthorizedAccessException or SecurityException or IOException;
}
