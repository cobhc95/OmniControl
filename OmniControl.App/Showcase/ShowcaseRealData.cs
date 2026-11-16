using OmniControl.App.Services;

namespace OmniControl.App.Showcase;

/// <summary>
/// The owner's settings for a <c>--real-data</c> export: a copy of the saved files (the stores of <see cref="DataStores"/> that are
/// <see cref="StoreKind.Saved"/>), so the run shows the real setup but can write nothing back. Nothing else is taken: no logs, no undo
/// copies, and none of the safety files that would make a start-up put Windows back (the effects journal, a pointer or button-swap
/// original). Windows Credential Manager is never read or copied: the run is given an empty credential store.
/// </summary>
internal static class ShowcaseRealData
{
    private const int Tries = 3;

    /// <summary>Copies the owner's saved settings into <paramref name="folder"/> (an existing, empty folder).</summary>
    public static void CopyInto(string folder)
    {
        var source = AppDataLocation.Default;
        foreach (var store in DataStores.All.Where(s => s.Kind == StoreKind.Saved && !s.IsFamily))
        {
            var from = Path.Combine(source, store.FileName);
            if (File.Exists(from)) CopyFile(from, Path.Combine(folder, store.FileName));
        }
    }

    /// <summary>A file the running app writes at that moment is tried again a moment later.</summary>
    private static void CopyFile(string from, string to)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(from, to, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < Tries)
            {
                Thread.Sleep(200 * attempt);
            }
        }
    }
}
