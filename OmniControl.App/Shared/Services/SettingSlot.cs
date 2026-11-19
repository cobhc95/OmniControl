namespace OmniControl.App.Shared.Services;

/// One field of the app's settings (settings.json) that a feature reads and writes without knowing the rest; writing saves at once.
public interface ISettingSlot<T>
{
    T Value { get; set; }
}

/// A field of <see cref="AppSettings"/>, read and written through <paramref name="read"/> and <paramref name="write"/>; the file's format is unchanged.
public sealed class SettingSlot<T>(AppSettings settings, Func<AppSettings, T> read, Action<AppSettings, T> write) : ISettingSlot<T>
{
    public T Value
    {
        get => read(settings);
        set
        {
            write(settings, value);
            settings.Save();
        }
    }
}
