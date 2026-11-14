using System.Diagnostics;

namespace OmniControl.Core.Engine;

/// <summary>What a program's .exe says about itself (reads the file only).</summary>
public static class ProgramFile
{
    /// <summary>The name a person knows the program by: the file's description ("Google Chrome"), else its file name without ".exe", capitalised.</summary>
    public static string NameOf(string path)
    {
        try
        {
            var d = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(d)) return d.Trim();
        }
        catch (FileNotFoundException) { /* the file is gone or never existed: use its name */ }
        catch (IOException) { /* locked or unreadable: use its name */ }
        catch (UnauthorizedAccessException) { /* not allowed to read it: use its name */ }
        var n = Path.GetFileNameWithoutExtension(path);
        return n.Length == 0 ? path : char.ToUpperInvariant(n[0]) + n[1..];
    }
}
