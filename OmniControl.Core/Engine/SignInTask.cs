using System.Security;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CSharp.RuntimeBinder;

namespace OmniControl.Core.Engine;

/// OmniControl's Task Scheduler task, which starts OmniControl as administrator without Windows' prompt. It has no trigger of its own: a
/// start that is not elevated (the start-up entry at sign-in, a shortcut, a restart) runs it with its own arguments ($(Arg0)), so
/// "Start with Windows" stays the one switch for starting at sign-in and nothing starts twice. Highest privileges, no time limit, also on
/// battery, normal priority (a task's default is below normal). Adding and removing it need administrator rights; running it does not.
/// Late-bound COM ("Schedule.Service"), as the firewall's. Call off the UI thread.
public static class SignInTask
{
    private const string Name = "OmniControl";

    /// Every start through the task carries it, so a copy the task started never runs the task again (no loop if Windows did not elevate it).
    public const string FromTask = "--from-task";

    private const int CreateOrUpdate = 6, InteractiveToken = 3, NoSuchTask = unchecked((int)0x80070002);
    private static readonly XNamespace Schema = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// What <see cref="Set"/> says for a copy in a folder its user may change (a portable or per-user copy).
    public const string NeedsInstalled = "OmniControl's sign-in task needs the installed version (a folder only administrators can change), so Windows asks for administrator rights at each start.";

    /// What to say after <see cref="RemoveUnsafe"/> removed a task.
    public const string RemovedUnsafe = "OmniControl's sign-in task was removed: it started a copy in a folder others can change. Windows asks for administrator rights at each start instead.";

    /// Adds the task for <paramref name="exe"/>, or points it there; null when done, else one plain sentence. Refused unless only
    /// administrators can change <paramref name="exe"/> (the task runs it elevated without a prompt, so a swappable exe would be a free elevation).
    public static string? Set(string exe)
    {
        if (!AdminOnly(exe)) return NeedsInstalled;
        return With(folder => Release(((dynamic)folder).RegisterTask(Name, Definition(exe), CreateOrUpdate, null, null, InteractiveToken, null))) is { } why
            ? $"Windows didn't add OmniControl's sign-in task ({why}), so it asks for administrator rights at each start."
            : null;
    }

    /// Removes the task when the program it starts, or a file or folder beside or under it, may be changed by its user (a copy made by an older
    /// version, or something written into the install folder later); the file or folder that did it when it removed the task, else null.
    public static string? RemoveUnsafe()
    {
        string? removed = null;
        With(folder =>
        {
            if (Find(folder) is not { } task) return;
            string? found;
            try
            {
                found = Command(task) is { } command ? Unsafe(command) : string.Empty;
            }
            finally { Release(task); }
            if (found is null) return;
            ((dynamic)folder).DeleteTask(Name, 0);
            removed = found;
        });
        return removed;
    }

    private static bool AdminOnly(string exe) => Unsafe(exe) is null;

    /// The first file or folder of <paramref name="exe"/>'s install its user may change, null when only administrators can change them all.
    private static string? Unsafe(string exe)
    {
        try
        {
            return exe.Length > 0 ? Resources.NetworkServiceSetup.Unsafe(exe, wholeFolder: true) : exe;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return exe; // not a path that can be shown to be safe
        }
    }

    /// Removes the task (nothing when there is none); null when done, else one plain sentence.
    public static string? Remove() =>
        With(folder =>
        {
            if (Find(folder) is not { } task) return;
            Release(task);
            ((dynamic)folder).DeleteTask(Name, 0);
        }) is { } why ? $"Windows didn't remove OmniControl's sign-in task ({why})." : null;

    /// True when the task is there and starts <paramref name="exe"/> (another copy's task is not this one's).
    public static bool IsFor(string exe) => Mine(exe, _ => { });

    /// Runs the task with these arguments when it starts <paramref name="exe"/> and only administrators can change that; false when there is none for it or Windows refused.
    public static bool Run(string exe, IEnumerable<string> arguments) => AdminOnly(exe) && Mine(exe, task => Release(((dynamic)task).Run(Elevation.Quoted(arguments))));

    /// Does <paramref name="use"/> with the task when it starts <paramref name="exe"/>; true when that went through.
    private static bool Mine(string exe, Action<object> use)
    {
        var done = false;
        With(folder =>
        {
            if (Find(folder) is not { } task) return;
            try
            {
                if (!Starts(task, exe)) return;
                use(task);
                done = true;
            }
            finally { Release(task); }
        });
        return done;
    }

    /// The task, or null when there is none; the caller releases it.
    private static object? Find(object folder)
    {
        try
        {
            return ((dynamic)folder).GetTask(Name);
        }
        catch (Exception ex) when (ex.HResult == NoSuchTask)
        {
            return null;
        }
    }

    private static bool Starts(object task, string exe) => string.Equals(Command(task), exe, StringComparison.OrdinalIgnoreCase);

    private static string? Command(object task) => XDocument.Parse((string)((dynamic)task).Xml).Descendants(Schema + "Command").FirstOrDefault()?.Value.Trim('"');

    /// Runs <paramref name="work"/> on the scheduler's root folder; null when it went through, else Windows' reason.
    private static string? With(Action<object> work)
    {
        object? service = null, folder = null;
        try
        {
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", throwOnError: true)!)!;
            ((dynamic)service).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            work(folder!);
            return null;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or IOException or RuntimeBinderException or InvalidCastException or XmlException)
        {
            return ex.Message;
        }
        finally
        {
            Release(folder);
            Release(service);
        }
    }

    /// Lets go of a COM object at once (its runtime wrapper would hold it until a garbage collection).
    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
    }

    private static string Definition(string exe) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="{Schema}">
          <RegistrationInfo><Description>Starts OmniControl as administrator without Windows' prompt (its Run as administrator setting).</Description></RegistrationInfo>
          <Principals><Principal id="Author"><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
          <Settings>
            <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>5</Priority>
          </Settings>
          <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(exe)}</Command><Arguments>{FromTask} $(Arg0)</Arguments><WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory></Exec></Actions>
        </Task>
        """;
}
