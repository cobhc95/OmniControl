using System.Text;
using OmniControl.Core.Engine.Apo;

namespace OmniControl.App.Features.Volume.Audio.Engine;

/// The only files OmniControl touches in an Equalizer APO config folder (the user's own Equalizer APO): it writes
/// the master's and the microphones' chains (<see cref="ApoChainWriter"/>, one Device section each) to its own
/// <see cref="ApoIds.ChainFileName"/> and puts ONE marked <see cref="ApoIds.IncludeLine"/> at the top of
/// config.txt (at the top, so no Device:/Channel: selection of the user's applies to it). Equalizer APO watches the folder and
/// reloads on every change. <see cref="Remove"/> takes out only those lines and deletes the chain file, so the user's own setup is
/// left as it was, including edits made in between. Before the first change config.txt is copied to
/// <see cref="ApoIds.ConfigBackupFileName"/>; the copy is deleted again once the file matches it. Bytes outside OmniControl's lines
/// are never re-encoded (Equalizer APO reads UTF-8 or the ANSI code page). Equalizer APO's installer lets the Users group write this
/// folder, so no administrator rights are needed. <see cref="Read"/> says what is really on disk (the keeper compares it with what
/// should be there), <see cref="Touch"/> makes Equalizer APO reload. The writers return an error sentence or null.
public static class ApoConfigFiles
{
    private const string NewLine = "\r\n";

    /// Reads every byte as one char and back, so text OmniControl does not own keeps its exact bytes.
    private static readonly Encoding Bytes = Encoding.Latin1;

    /// The UTF-8 byte order mark as <see cref="Bytes"/> reads it.
    private static readonly string Bom = Bytes.GetString(Encoding.UTF8.Preamble);

    /// Writes the chain (only when it differs, so an unchanged file causes no reload) and makes sure config.txt includes it, at its top.
    public static string? Apply(string folder, string chainText)
    {
        try
        {
            if (LinkIn(folder) is { } refused) return refused;
            var chain = Path.Combine(folder, ApoIds.ChainFileName);
            var bytes = Encoding.UTF8.GetBytes(chainText);
            if (!File.Exists(chain) || !File.ReadAllBytes(chain).AsSpan().SequenceEqual(bytes)) WriteShut(chain, bytes);
            PlaceInclude(folder);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't write to Equalizer APO's config folder ({folder}): {ex.Message}";
        }
    }

    /// What is on disk in <paramref name="folder"/> now: whether OmniControl's part is as wanted (<paramref name="wanted"/> = the chain text,
    /// with the include at the top of config.txt; null = none of OmniControl's lines and no chain file), and whether config.txt runs
    /// effects of its own. Null when a file is busy or can't be read right now (asked again later).
    public static ApoFolderFacts? Read(string folder, string? wanted)
    {
        try
        {
            var config = Path.Combine(folder, ApoIds.ConfigFileName);
            var text = File.Exists(config) ? Bytes.GetString(File.ReadAllBytes(config)) : string.Empty;
            var lines = SplitLines(text[BomOf(text).Length..]).ToList();
            var own = lines.Any(line => !IsOurs(line) && IsCommand(line));
            var chain = Path.Combine(folder, ApoIds.ChainFileName);
            var inPlace = wanted is null
                ? !lines.Any(IsOurs) && !File.Exists(chain)
                : Placed(text) == text && File.Exists(chain) && File.ReadAllBytes(chain).AsSpan().SequenceEqual(Encoding.UTF8.GetBytes(wanted));
            return new ApoFolderFacts(inPlace, own);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Equalizer APO or its editor holds a file this moment (asked again later); anything else is logged.
            if (ex is not IOException busy || !IsSharingViolation(busy)) AppDataStore.AppendLog(Guard.LogFile, $"Equalizer APO's config folder ({folder}) could not be read: {ex.Message}");
            return null;
        }
    }

    /// Makes Equalizer APO load the folder again without changing a byte: OmniControl's chain file gets a new write time (Equalizer APO
    /// watches the folder for writes). Nothing happens when the file is not there.
    public static string? Touch(string folder)
    {
        try
        {
            var chain = Path.Combine(folder, ApoIds.ChainFileName);
            if (File.Exists(chain)) File.SetLastWriteTimeUtc(chain, DateTime.UtcNow);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Couldn't ask Equalizer APO to reload ({folder}): {ex.Message}";
        }
    }

    /// Takes OmniControl's lines out of config.txt and deletes the chain file; nothing happens when they are not there.
    public static string? Remove(string folder)
    {
        try
        {
            if (LinkIn(folder) is { } refused) return refused;
            var config = Path.Combine(folder, ApoIds.ConfigFileName);
            if (File.Exists(config))
            {
                var text = Bytes.GetString(File.ReadAllBytes(config));
                var kept = Without(text);
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

    /// Puts OmniControl's marker and include line at the very top of config.txt, once: above any Device:, Channel: or Stage: line of the
    /// user's, which would otherwise limit or reshape OmniControl's sections. A line found lower down (moved, or written back by Equalizer
    /// APO's editor) or twice is taken out first. The user's file before OmniControl's first line is kept as the backup.
    private static void PlaceInclude(string folder)
    {
        var config = Path.Combine(folder, ApoIds.ConfigFileName);
        var text = File.Exists(config) ? Bytes.GetString(File.ReadAllBytes(config)) : string.Empty;
        var placed = Placed(text);
        if (placed == text) return;
        var backup = Path.Combine(folder, ApoIds.ConfigBackupFileName);
        var users = Without(text);
        if (users.Length > 0 && !File.Exists(backup)) WriteShut(backup, Bytes.GetBytes(users));
        WriteShut(config, Bytes.GetBytes(placed));
    }

    /// <paramref name="text"/> with OmniControl's two lines at the top and nowhere else.
    private static string Placed(string text)
    {
        var bom = BomOf(text);
        return bom + ApoIds.IncludeMarker + NewLine + ApoIds.IncludeLine + NewLine + Without(text)[bom.Length..];
    }

    /// <paramref name="text"/> without any of OmniControl's lines (its byte order mark kept).
    private static string Without(string text)
    {
        var bom = BomOf(text);
        return bom + string.Concat(SplitLines(text[bom.Length..]).Where(line => !IsOurs(line)));
    }

    /// A line Equalizer APO acts on (not blank, not a comment).
    private static bool IsCommand(string line) => line.Trim() is { Length: > 0 } trimmed && !trimmed.StartsWith('#');

    private static string BomOf(string text) => text.StartsWith(Bom, StringComparison.Ordinal) ? Bom : string.Empty;

    /// The backup is only kept while config.txt differs from it (the user changed something in between: their file wins).
    private static void DropBackupIfSame(string folder, string text)
    {
        var backup = Path.Combine(folder, ApoIds.ConfigBackupFileName);
        if (File.Exists(backup) && Bytes.GetString(File.ReadAllBytes(backup)) == text) File.Delete(backup);
    }

    private static bool IsInclude(string line) => line.Trim().Equals(ApoIds.IncludeLine, StringComparison.OrdinalIgnoreCase);
    private static bool IsOurs(string line) => IsInclude(line) || line.Trim() is ApoIds.IncludeMarker or ApoIds.PreviousIncludeMarker;

    /// The lines of <paramref name="text"/>, each with its own line break, so joining them gives the text back exactly.
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

    /// Writes with no sharing, so Equalizer APO never reads half a file (it waits while the file is busy); if it is reading at
    /// that moment, waits <see cref="Timing.ApoFileRetry"/> and tries again.
    private static void WriteShut(string path, byte[] bytes)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var file = PlainFile.OpenToWrite(path, FileShare.None); // checked through the handle it writes with: never through a link
                file.Write(bytes);
                return;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < Timing.ApoFileAttempts)
            {
                Thread.Sleep(Timing.ApoFileRetry);
            }
        }
    }

    /// One sentence when a file OmniControl writes or deletes in the folder (or the folder) is a link: anyone may write this folder, so a
    /// planted link would make OmniControl change the file it points to. Null when every one is a plain file.
    private static string? LinkIn(string folder) =>
        new[] { ApoIds.ConfigFileName, ApoIds.ChainFileName, ApoIds.ConfigBackupFileName }.Any(name => !PlainFile.IsPlain(Path.Combine(folder, name)))
            ? $"Equalizer APO's config folder ({folder}) holds a link where its config files belong, so OmniControl left it alone."
            : null;

    /// ERROR_SHARING_VIOLATION (32) as an HRESULT.
    private static bool IsSharingViolation(IOException ex) => (ex.HResult & 0xFFFF) == 32;
}
