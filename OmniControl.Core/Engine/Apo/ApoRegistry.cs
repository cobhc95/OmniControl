using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;

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
    /// Device Selector's takeOwnership + makeWritable: Administrators own the key and get full access to it and what is under it.
    void MakeWritable(string key);
}

/// <see cref="IApoRegistry"/> on Windows' registry. Writes need administrator rights (the elevated helper, <see cref="ApoSetup"/>).
internal sealed class MachineRegistry : IApoRegistry
{
    private static RegistryKey Machine() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

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

    public string? ReadString(string key, string name)
    {
        using var machine = Machine();
        using var k = machine.OpenSubKey(key);
        return k?.GetValue(name) switch { string s => s, string[] { Length: > 0 } multi => multi[0], _ => null };
    }

    public uint? ReadDword(string key, string name)
    {
        using var machine = Machine();
        using var k = machine.OpenSubKey(key);
        return k?.GetValue(name) is int v ? unchecked((uint)v) : null;
    }

    public void WriteString(string key, string name, string value) => Write(key, name, value, RegistryValueKind.String);

    public void WriteMulti(string key, string name, string value) => Write(key, name, new[] { value }, RegistryValueKind.MultiString);

    private static void Write(string key, string name, object value, RegistryValueKind kind)
    {
        using var machine = Machine();
        using var k = machine.CreateSubKey(key, writable: true);
        k.SetValue(name, value, kind);
    }

    public void DeleteValue(string key, string name)
    {
        using var machine = Machine();
        using var k = machine.OpenSubKey(key, writable: true);
        k?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void CreateKey(string key)
    {
        using var machine = Machine();
        using var k = machine.CreateSubKey(key, writable: true);
    }

    public void DeleteKey(string key)
    {
        using var machine = Machine();
        machine.DeleteSubKey(key, throwOnMissingSubKey: false);
    }

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

    public void MakeWritable(string key)
    {
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        Privileges.Enable(Privileges.TakeOwnership);
        using var machine = Machine();
        using (var owned = machine.OpenSubKey(key, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.TakeOwnership))
        {
            if (owned is null) throw new IOException($"The key {key} is missing.");
            var security = owned.GetAccessControl(AccessControlSections.None);
            security.SetOwner(admins);
            owned.SetAccessControl(security);
        }
        using var writable = machine.OpenSubKey(key, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ReadPermissions | RegistryRights.ChangePermissions)
            ?? throw new IOException($"The key {key} is missing.");
        var acl = writable.GetAccessControl(AccessControlSections.Access);
        acl.SetAccessRule(new RegistryAccessRule(admins, RegistryRights.FullControl, InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
        writable.SetAccessControl(acl);
    }

    /// The failures a registry call reports (missing rights, a key in use); everything else is a bug and is not caught.
    public static bool IsRegistryFailure(Exception ex) => ex is UnauthorizedAccessException or SecurityException or IOException;
}
