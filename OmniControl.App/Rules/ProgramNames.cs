using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace OmniControl.App.Rules;

/// <summary>
/// Program names as rules use them: lower case, without folder or ".exe" ("chrome"). <see cref="NameOf"/> answers for a process id
/// (asked by the engine for the window under the pointer, cached so a program is looked up once); <see cref="Known"/> lists the
/// programs to offer in the condition picker.
/// </summary>
public sealed class ProgramNames
{
    // A process id can be reused by a new program after the old one ends; the cache is emptied now and then so a stale name does not last.
    private const int CacheLimit = 512;
    private readonly ConcurrentDictionary<int, string> _byId = new();

    /// <summary>The one way a typed or found program name is compared: "C:\Apps\Chrome.exe", "chrome.exe" and "chrome" are the same.</summary>
    public static string Normalize(string program) => Path.GetFileNameWithoutExtension(program.Trim()).ToLowerInvariant();

    /// <summary>The name of the program with this process id, or empty when it has ended or can't be read.</summary>
    public string NameOf(int processId)
    {
        if (processId == 0) return string.Empty;
        if (_byId.TryGetValue(processId, out var known)) return known;
        if (_byId.Count > CacheLimit) _byId.Clear();
        var name = string.Empty;
        try
        {
            using var process = Process.GetProcessById(processId);
            name = Normalize(process.ProcessName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // The program ended between the question and the answer, or Windows won't say: it matches no program condition.
        }
        _byId[processId] = name;
        return name;
    }

    /// <summary>Programs with a window now, plus <paramref name="remembered"/> (paths or names), each once, in alphabetical order.</summary>
    public static IReadOnlyList<string> Known(IEnumerable<string> remembered)
    {
        var names = new SortedSet<string>(remembered.Where(r => r.Trim().Length > 0).Select(Normalize));
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id != Environment.ProcessId && process.MainWindowHandle != IntPtr.Zero) names.Add(Normalize(process.ProcessName));
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // It ended while the list was read, or it belongs to another user: left out.
                }
            }
        }
        return names.ToList();
    }
}
