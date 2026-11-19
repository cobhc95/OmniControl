using System.Collections.Concurrent;
using OmniControl.Core.Engine.Mouse;

namespace OmniControl.Core.Engine;

/// A program with a window now: its identity (<see cref="ProgramIdentity.Normalize"/>) and the name a person knows it by.
public sealed record RunningProgram(string Id, string Name);

/// How a program is identified everywhere (rules, automation, app lists): its executable's name in lower case, without folder or ".exe"
/// ("chrome"). <see cref="NameOf"/> answers for a process id and <see cref="Foreground"/> for the program the user is working in; both are
/// cheap enough for the mouse hook (a program is looked up once, then cached), so one instance should live as long as its asker.
public sealed class ProgramIdentity
{
    // A process id can be reused by a new program after the old one ends; the cache is emptied now and then so a stale name does not last.
    private const int CacheLimit = 512;
    private readonly ConcurrentDictionary<int, string> _byId = new();

    /// The one way a typed or found program name is compared: "C:\Apps\Chrome.exe", "chrome.exe" and "chrome" are the same.
    public static string Normalize(string program) => Path.GetFileNameWithoutExtension(program.Trim()).ToLowerInvariant();

    /// The name of the program with this process id, or empty when it has ended or can't be read.
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
            // The program ended between the question and the answer, or Windows won't say: it matches no program.
        }
        _byId[processId] = name;
        return name;
    }

    /// The program the user is working in (the active window's), or empty when there is none or it can't be read.
    public string Foreground() => NameOf(WindowQuery.ActiveProcessId());

    /// The programs with a window now (OmniControl itself left out), each once, in alphabetical order of their names.
    public static IReadOnlyList<RunningProgram> Running()
    {
        var found = new SortedDictionary<string, RunningProgram>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.Id == Environment.ProcessId || process.MainWindowHandle == IntPtr.Zero) continue;
                    var id = Normalize(process.ProcessName);
                    if (id.Length == 0 || found.Values.Any(p => p.Id == id)) continue;
                    var path = process.MainModule?.FileName;
                    var name = path is null ? Capitalised(id) : ProgramFile.NameOf(path);
                    found[$"{name}|{id}"] = new RunningProgram(id, name);
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                    // It ended while the list was read, or it belongs to another user: left out.
                }
            }
        }
        return found.Values.ToList();
    }

    private static string Capitalised(string id) => char.ToUpperInvariant(id[0]) + id[1..];
}
