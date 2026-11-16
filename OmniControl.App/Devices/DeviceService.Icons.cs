namespace OmniControl.App.Devices;

// A device's own icon image: copied into the app's data when picked (so moving or deleting the original never breaks it), removed with the device.
public sealed partial class DeviceService
{
    /// <summary>Copies the picture at <paramref name="path"/> as the device's icon. Null when it worked, otherwise one plain sentence.</summary>
    public string? SetOwnImage(DeviceRecord device, string path)
    {
        try
        {
            Directory.CreateDirectory(DeviceIcons.ImageFolder);
            var file = $"{device.Id}{Path.GetExtension(path).ToLowerInvariant()}";
            DeleteOwnImage(device);
            File.Copy(path, Path.Combine(DeviceIcons.ImageFolder, file), overwrite: true);
            device.IconImage = file;
            Edited();
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Log($"Copying the icon image {path}: {ex.Message}");
            return "That picture couldn't be copied. Try another one.";
        }
    }

    /// <summary>Back to a drawn icon: the copied picture is deleted.</summary>
    public void ClearOwnImage(DeviceRecord device)
    {
        DeleteOwnImage(device);
        device.IconImage = string.Empty;
        Edited();
    }

    /// <summary>The device's copied picture, or null when it has none.</summary>
    public static string? OwnImagePath(DeviceRecord device) =>
        device.IconImage.Length > 0 && File.Exists(Path.Combine(DeviceIcons.ImageFolder, device.IconImage)) ? Path.Combine(DeviceIcons.ImageFolder, device.IconImage) : null;

    private static void DeleteOwnImage(DeviceRecord device)
    {
        if (OwnImagePath(device) is not { } path) return;
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log($"Deleting the icon image {path}: {ex.Message}"); // a picture left behind costs only its space
        }
    }
}
