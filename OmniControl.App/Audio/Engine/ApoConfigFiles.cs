using System.Text;
using OmniControl.App.Services;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Audio.Engine;

/// <summary>
/// The only files OmniControl touches in an Equalizer APO config folder (the user's own Equalizer APO, or OmniControl APO): it writes
/// the master's and the microphones' chains (<see cref="ApoChainWriter"/>, one Device section each) to its own
/// <see cref="ApoIds.ChainFileName"/> and puts ONE marked <see cref="ApoIds.IncludeLine"/> at the top of
/// config.txt (at the top, so no Device:/Channel: selection of the user's applies to it). Equalizer APO watches the folder and
/// reloads on every change. <see cref="Remove"/> takes out only those lines and deletes the chain file, so the user's own setup is
/// left as it was, including edits made in between. Before the first change config.txt is copied to
/// <see cref="ApoIds.ConfigBackupFileName"/>; the copy is deleted again once the file matches it. Bytes outside OmniControl's lines
/// are never re-encoded (Equalizer APO reads UTF-8 or the ANSI code page). Equalizer APO's installer lets the Users group write this
/// folder, so no administrator rights are needed. Each method returns an error sentence or null.
/// </summary>
public static class ApoConfigFiles
{
    private const string NewLine = "\r\n";

    /// <summary>Reads every byte as one char and back, so text OmniControl does not own keeps its exact bytes.</summary>
    private static readonly Encoding Bytes = Encoding.Latin1;

    /// <summary>The UTF-8 byte order mark as <see cref="Bytes"/> reads it.</summary>
    private static readonly string Bom = Bytes.GetString(Encoding.UTF8.Preamble);

    /// <summary>Writes the chain and makes sure config.txt includes it.</summary>
    public static string? Apply(string folder, string chainText)
    {
        try
        {
            WriteShut(Path.Combine(folder, ApoIds.ChainFileName), Encoding.UTF8.GetBytes(chainText));
            AddInclude(folder);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't write to Equalizer APO's config folder ({folder}): {ex.Message}";
        }
    }

    /// <summary>Takes OmniControl's lines out of config.txt and deletes the chain file; nothing happens when they are not there.</summary>
    public static string? Remove(string folder)
    {
        try
        {
            var config = Path.Combine(folder, ApoIds.ConfigFileName);
            if (File.Exists(config))
            {
                var text = Bytes.GetString(File.ReadAllBytes(config));
                var bom = BomOf(text);
                var kept = bom + string.Concat(SplitLines(text[bom.Length..]).Where(line => !IsOurs(line)));
                if (kept != text) WriteShut(config, Bytes.GetBytes(kept));
                DropBackupIfSame(folder, kept);
            }
            var chain = Path.Combine(folder, ApoIds.ChainFileName);
            if (File.Exists(chain)) File.Delete(chain);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't take OmniControl's line out of Equalizer APO's config ({folder}): {ex.Message}";
        }
    }

    private static void AddInclude(string folder)
    {
        var config = Path.Combine(folder, ApoIds.ConfigFileName);
        var text = File.Exists(config) ? Bytes.GetString(File.ReadAllBytes(config)) : string.Empty;
        var bom = BomOf(text);
        if (SplitLines(text[bom.Length..]).Any(IsInclude)) return; // already there (perhaps moved by the user: left where it is)
        var backup = Path.Combine(folder, ApoIds.ConfigBackupFileName);
        if (text.Length > 0 && !File.Exists(backup)) File.WriteAllBytes(backup, Bytes.GetBytes(text));
        var ours = ApoIds.IncludeMarker + NewLine + ApoIds.IncludeLine + NewLine;
        WriteShut(config, Bytes.GetBytes(bom + ours + text[bom.Length..]));
    }

    private static string BomOf(string text) => text.StartsWith(Bom, StringComparison.Ordinal) ? Bom : string.Empty;

    /// <summary>The backup is only kept while config.txt differs from it (the user changed something in between: their file wins).</summary>
    private static void DropBackupIfSame(string folder, string text)
    {
        var backup = Path.Combine(folder, ApoIds.ConfigBackupFileName);
        if (File.Exists(backup) && Bytes.GetString(File.ReadAllBytes(backup)) == text) File.Delete(backup);
    }

    private static bool IsInclude(string line) => line.Trim().Equals(ApoIds.IncludeLine, StringComparison.OrdinalIgnoreCase);
    private static bool IsOurs(string line) => IsInclude(line) || line.Trim() is ApoIds.IncludeMarker or ApoIds.PreviousIncludeMarker;

    /// <summary>The lines of <paramref name="text"/>, each with its own line break, so joining them gives the text back exactly.</summary>
    private static IEnumerable<string> SplitLines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            yield return text[start..(i + 1)];
            start = i + 1;
        }
        if (start < text.Length) yield return text[start..];
    }

    /// <summary>
    /// Writes with no sharing, so Equalizer APO never reads half a file (it waits while the file is busy); if it is reading at
    /// that moment, waits <see cref="Timing.ApoFileRetry"/> and tries again.
    /// </summary>
    private static void WriteShut(string path, byte[] bytes)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                file.Write(bytes);
                return;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < Timing.ApoFileAttempts)
            {
                Thread.Sleep(Timing.ApoFileRetry);
            }
        }
    }

    /// <summary>ERROR_SHARING_VIOLATION (32) as an HRESULT.</summary>
    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) == 32;
}
