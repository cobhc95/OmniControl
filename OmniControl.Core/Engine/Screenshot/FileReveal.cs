namespace OmniControl.Core.Engine.Screenshot;

/// Opens File Explorer at a file's folder with the file selected.
public static class FileReveal
{
    /// Null when Explorer was started, otherwise why not.
    public static string? Show(string file)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/select,\"{file}\"") { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"Couldn't open the folder: {ex.Message}";
        }
    }
}
