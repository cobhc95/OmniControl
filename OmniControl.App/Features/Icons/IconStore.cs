using System.Collections.Specialized;
using Avalonia.Media.Imaging;
using OmniControl.App.Features.Hotkeys;

namespace OmniControl.App.Features.Icons;

/// One icon of the user's ("Your icons"): its id, the name they gave it, and the drawing itself, kept in
/// icons.json so a backup carries it: an SVG's shapes (path data in its own view box, outline or filled) or a PNG made small, as base64.
public sealed class UserIcon
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PathData { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool Stroked { get; set; }
    public double StrokeWidth { get; set; }
    public string? Png { get; set; }

    /// What is saved where this icon is used.
    public string Value => IconStore.UserPrefix + Id;
}

/// icons.json: the user's own icons, and the icon chosen for an action (by action id; none = its default).
public sealed class IconSettings
{
    public List<UserIcon> Mine { get; set; } = new();
    public Dictionary<string, string> Actions { get; set; } = new();
}

/// The user's own icons and the icons chosen for actions (icons.json, <see cref="DataStores.Icons"/>, backed up). Each user icon is put in
/// the application's resources under its value, so <see cref="IconResources"/> draws it like a library icon everywhere. It gives every
/// action its chosen icon as the action is registered (<see cref="HotkeyAction.ChosenIcon"/>), so registration order does not matter.
public sealed class IconStore : IDisposable
{
    public const string UserPrefix = "user:";

    /// A picture bigger than this is refused; a PNG is kept at most this many pixels on its long side.
    private const long MaxFileBytes = 2 << 20;
    private const int PngSide = 64;

    /// A PNG whose header states more than this on a side, or more pixels than this in all, is refused before it is decoded.
    internal const int MaxSide = 8192, MaxPixels = 16 << 20;

    private const string Unreadable = "That picture couldn't be read as an icon.";

    private readonly IActionCatalogue _hotkeys;
    private readonly IconSettings _settings = AppDataStore.Load<IconSettings>(DataStores.Icons) ?? new();

    public IconStore(IActionCatalogue hotkeys)
    {
        _hotkeys = hotkeys;
        foreach (var icon in _settings.Mine) Publish(icon);
        foreach (var action in hotkeys.Actions) Apply(action);
        hotkeys.Actions.CollectionChanged += OnActionsChanged;
    }

    /// The user's icons, in the order they were added.
    public IReadOnlyList<UserIcon> Mine => _settings.Mine;

    /// A user icon was added, renamed or removed, or an action's icon was chosen: what shows icons draws them again.
    public event Action? Changed;

    /// The name of an icon value: a library icon's, a user icon's, or empty.
    public string NameOf(string? value) => IconLibrary.Find(value)?.Name ?? Find(value)?.Name ?? string.Empty;

    /// Adds the SVG or PNG at <paramref name="path"/> to "Your icons" (named after the file) and returns its value, or one plain sentence why not.
    public (string? Value, string? Problem) Import(string path)
    {
        try
        {
            if (new FileInfo(path).Length > MaxFileBytes) return (null, "That file is too big for an icon. Pick one under 2 MB.");
            var icon = new UserIcon { Id = Guid.NewGuid().ToString("N")[..12], Name = Path.GetFileNameWithoutExtension(path) };
            if (string.Equals(Path.GetExtension(path), ".svg", StringComparison.OrdinalIgnoreCase))
            {
                if (SvgShapes.Read(File.ReadAllText(path)) is not { } drawing) return (null, "That SVG has no shapes OmniControl can draw.");
                (icon.PathData, icon.X, icon.Y, icon.Width, icon.Height, icon.Stroked, icon.StrokeWidth) =
                    (drawing.PathData, drawing.X, drawing.Y, drawing.Width, drawing.Height, drawing.Stroked, drawing.StrokeWidth);
            }
            else
            {
                var (png, problem) = SmallPng(File.ReadAllBytes(path));
                if (png is null) return (null, problem);
                icon.Png = png;
            }
            if (Image(icon) is not { } image) return (null, Unreadable);
            _settings.Mine.Add(icon);
            Show(icon.Value, image);
            Save();
            return (icon.Value, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return (null, "That picture couldn't be read. Try another one."); // an unreadable or odd file: nothing is added
        }
    }

    /// Takes icons.json from disk as it is now (an import wrote it): the user's icons and every action's chosen icon follow, and what shows icons draws them again.
    public bool ReloadFromDisk()
    {
        foreach (var icon in _settings.Mine) Retire(icon.Value);
        SettingsCopy.Into(_settings, AppDataStore.Load<IconSettings>(DataStores.Icons) ?? new());
        foreach (var icon in _settings.Mine) Publish(icon);
        foreach (var action in _hotkeys.Actions) Apply(action);
        Changed?.Invoke();
        return true;
    }

    public void Rename(string value, string name)
    {
        if (Find(value) is not { } icon || name.Trim().Length == 0 || icon.Name == name.Trim()) return;
        icon.Name = name.Trim();
        Save();
    }

    /// Takes a user icon out; whatever showed it shows its default again.
    public void Remove(string value)
    {
        if (Find(value) is not { } icon) return;
        _settings.Mine.Remove(icon);
        Retire(value);
        foreach (var id in _settings.Actions.Where(p => p.Value == value).Select(p => p.Key).ToList()) _settings.Actions.Remove(id);
        foreach (var action in _hotkeys.Actions.Where(a => a.ChosenIcon == value)) action.ChosenIcon = null;
        Save();
    }

    /// The icon the user chose for an action (null: back to its default).
    public void ChooseForAction(HotkeyAction action, string? value)
    {
        if (value is null) _settings.Actions.Remove(action.Id);
        else _settings.Actions[action.Id] = value;
        action.ChosenIcon = value;
        Save();
    }

    private UserIcon? Find(string? value) =>
        value is not null && value.StartsWith(UserPrefix, StringComparison.Ordinal) ? _settings.Mine.FirstOrDefault(i => i.Value == value) : null;

    private void Apply(HotkeyAction action) => action.ChosenIcon = _settings.Actions.GetValueOrDefault(action.Id);

    private void OnActionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var action in e.NewItems?.OfType<HotkeyAction>() ?? Enumerable.Empty<HotkeyAction>()) Apply(action);
    }

    private static void Publish(UserIcon icon)
    {
        if (Image(icon) is { } image) Show(icon.Value, image);
    }

    // The application's resources own each shown picture: it is disposed when it is replaced or taken out (Retire).
    private static void Show(string value, IconImage image)
    {
        if (Application.Current is not { } app)
        {
            (image.Picture as IDisposable)?.Dispose();
            return;
        }
        Retire(value);
        app.Resources[value] = image;
    }

    // Disposed after the current UI work, so what showed it has already followed Changed and drawn its replacement or default.
    private static void Retire(string value)
    {
        if (Application.Current is not { } app || !app.Resources.TryGetValue(value, out var old)) return;
        app.Resources.Remove(value);
        if (old is IconImage { Picture: IDisposable picture }) Dispatcher.UIThread.Post(picture.Dispose, DispatcherPriority.Background);
    }

    /// What the icon draws: its shapes on the 24 x 24 grid of the library, or its picture; null when it cannot be read.
    private static IconImage? Image(UserIcon icon)
    {
        try
        {
            if (icon.Png is { } png)
            {
                using var stream = new MemoryStream(Convert.FromBase64String(png));
                return IconImage.Of(IconPictures.Decode(stream, "IconCellGlyph", fill: false)); // the icon picker's cells are the largest it is drawn
            }
            if (icon.PathData is not { } data || icon.Width <= 0 || icon.Height <= 0) return null;
            var shape = Geometry.Parse(data);
            var scale = 24 / Math.Max(icon.Width, icon.Height);
            shape.Transform = new MatrixTransform(Matrix.CreateTranslation(-icon.X - icon.Width / 2, -icon.Y - icon.Height / 2)
                * Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(12, 12));
            return icon.Stroked ? IconImage.Outline(shape, icon.StrokeWidth * scale) : IconImage.Filled(shape);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            return null; // damaged saved data: the icon is not drawn and its users fall back to their defaults
        }
    }

    /// The PNG in <paramref name="file"/> decoded straight to at most <see cref="PngSide"/> pixels on its long side, as base64 PNG; or why not.
    internal static (string? Png, string? Problem) SmallPng(byte[] file)
    {
        if (IconPictures.PngSize(file) is not { } size) return (null, Unreadable);
        if (size.Width > MaxSide || size.Height > MaxSide || (long)size.Width * size.Height > MaxPixels)
            return (null, "That picture is too large for an icon. Pick a smaller one.");
        using var source = new MemoryStream(file, writable: false);
        using var small = Math.Max(size.Width, size.Height) <= PngSide ? new Bitmap(source)
            : size.Width >= size.Height ? Bitmap.DecodeToWidth(source, PngSide) : Bitmap.DecodeToHeight(source, PngSide);
        using var stream = new MemoryStream();
        small.Save(stream);
        return (Convert.ToBase64String(stream.ToArray()), null);
    }

    private void Save()
    {
        AppDataStore.SaveSoon(DataStores.Icons, _settings);
        Changed?.Invoke();
    }

    public void Dispose() => _hotkeys.Actions.CollectionChanged -= OnActionsChanged;
}
