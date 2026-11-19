namespace OmniControl.App.Shared.Scripts;

/// Program names as rules use them (<see cref="ProgramIdentity"/> owns the rule: lower case, without folder or ".exe", "chrome").
/// <see cref="NameOf"/> answers for a process id (asked by the engine for the window under the pointer, cached so a program is looked
/// up once); <see cref="Known"/> lists the programs to offer in the condition picker.
public sealed class ProgramNames
{
    private readonly ProgramIdentity _identity = new();

    /// The one way a typed or found program name is compared: "C:\Apps\Chrome.exe", "chrome.exe" and "chrome" are the same.
    public static string Normalize(string program) => ProgramIdentity.Normalize(program);

    /// The name of the program with this process id, or empty when it has ended or can't be read.
    public string NameOf(int processId) => _identity.NameOf(processId);

    /// Programs with a window now, plus <paramref name="remembered"/> (paths or names), each once, in alphabetical order.
    public static IReadOnlyList<string> Known(IEnumerable<string> remembered)
    {
        var names = new SortedSet<string>(remembered.Where(r => r.Trim().Length > 0).Select(Normalize));
        foreach (var program in ProgramIdentity.Running()) names.Add(program.Id);
        return names.ToList();
    }
}
