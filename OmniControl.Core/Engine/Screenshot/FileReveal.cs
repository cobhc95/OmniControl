using System.ComponentModel;
using System.Diagnostics;

namespace OmniControl.Core.Engine.Screenshot;

/// <summary>Opens File Explorer at a file's folder with the file selected.</summary>
public static class FileReveal
{
    /// <summary>Null when Explorer was started, otherwise why not.</summary>
    public static string? Show(string file)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true });
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return $"Couldn't open the folder: {ex.Message}";
        }
    }
}
