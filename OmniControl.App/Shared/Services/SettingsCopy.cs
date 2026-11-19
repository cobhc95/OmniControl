using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization;

namespace OmniControl.App.Shared.Services;

/// The one way a service takes a freshly read settings object into the one it already holds (a reload from disk, see
/// <see cref="Features.OtherPcs.SettingsImport.ImportHooks"/>): every saved property of <paramref name="target"/> is set from <paramref name="source"/>. The target object
/// stays, so whatever holds it sees the change; a nested settings object is copied the same way (it stays too); a list or dictionary is
/// replaced by the new one. Pure: it reads and writes properties only.
public static class SettingsCopy
{
    public static void Into<T>(T target, T source) where T : class => CopyObject(target, source);

    private static void CopyObject(object target, object source)
    {
        foreach (var property in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0) continue;
            if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always }) continue;
            var value = property.GetValue(source);
            if (value is not null && property.GetValue(target) is { } own && IsSettingsObject(property.PropertyType) && own.GetType() == value.GetType())
                CopyObject(own, value);
            else property.SetValue(target, value);
        }
    }

    private static bool IsSettingsObject(Type type) =>
        type.IsClass && type != typeof(string) && !typeof(IEnumerable).IsAssignableFrom(type) && type.GetConstructor(Type.EmptyTypes) is not null;
}
