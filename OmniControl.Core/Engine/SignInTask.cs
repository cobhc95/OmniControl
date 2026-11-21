using System.Security;
using System.Security.Principal;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CSharp.RuntimeBinder;

namespace OmniControl.Core.Engine;

/// OmniControl's Task Scheduler task, which starts OmniControl as administrator without Windows' prompt. It has no trigger of its own: a
/// start that is not elevated (the start-up entry at sign-in, a shortcut, a restart) runs it with its own arguments ($(Arg0)), so
/// "Start with Windows" stays the one switch for starting at sign-in and nothing starts twice. Highest privileges, no time limit, also on
/// battery, normal priority (a task's default is below normal). Only Setup adds it (its "Start OmniControl as administrator at sign-in"
/// task, elevated), for the user signed in to the session Setup runs in, also when another account answered Windows' prompt. Adding and
/// removing it need administrator rights; running it does not. Late-bound COM ("Schedule.Service"), as the firewall's. Call off the UI thread.
public static class SignInTask
{
    private const string Name = "OmniControl";

    /// Every start through the task carries it, so a copy the task started never runs the task again (no loop if Windows did not elevate it).
    public const string FromTask = "--from-task";

    private const string Arguments = FromTask + " $(Arg0)", LogonType = "InteractiveToken", RunLevel = "HighestAvailable";
    private const int CreateOrUpdate = 6, InteractiveToken = 3, NoSuchTask = unchecked((int)0x80070002);
    private static readonly XNamespace Schema = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    /// What to say after <see cref="RemoveInvalid"/> removed a task.
    public const string Removed = "OmniControl's sign-in task was removed because it wasn't the one Setup makes, so Windows asks for administrator rights at each start.";

    /// Adds the task for <paramref name="exe"/> and the user signed in here, or points it there; null when done, else one plain sentence.
    /// Refused unless only administrators can change <paramref name="exe"/> (the task runs it elevated without a prompt, so a swappable exe
    /// would be a free elevation).
    public static string? Set(string exe)
    {
        if (Unsafe(exe) is not null) return "OmniControl's sign-in task needs the installed version (a folder only administrators can change).";
        if (SessionUser() is not { } user) return "Windows didn't say who is signed in here, so OmniControl's sign-in task was not added.";
        return With(folder => Release(((dynamic)folder).RegisterTask(Name, Definition(exe, user), CreateOrUpdate, null, null, InteractiveToken, null))) is { } why
            ? $"Windows didn't add OmniControl's sign-in task ({why})."
            : null;
    }

    /// Removes the task unless it is the one <see cref="Set"/> makes: one program only administrators can change, OmniControl's arguments,
    /// no trigger, one interactive principal at the highest level; with <paramref name="install"/> (Setup's check of a task from before) also
    /// that it starts that exe and is for the user signed in here. <c>Wrong</c>: what was wrong with it (null when nothing, or no task);
    /// <c>Failed</c>: Windows' reason when it could not be read or removed.
    public static (string? Wrong, string? Failed) RemoveInvalid(string? install)
    {
        string? wrong = null;
        var failed = With(folder =>
        {
            if (Find(folder) is not { } task) return;
            try
            {
                wrong = Problem(XDocument.Parse((string)((dynamic)task).Xml), install);
            }
            finally { Release(task); }
            if (wrong is not null) ((dynamic)folder).DeleteTask(Name, 0);
        });
        return (wrong, failed);
    }

    private static string? Problem(XDocument task, string? install)
    {
        var actions = task.Descendants(Schema + "Actions").Elements().ToList();
        if (actions.Count != 1 || actions[0].Name != Schema + "Exec") return "it does not start exactly one program";
        var command = Value(actions[0], "Command").Trim('"');
        if (install is not null && !string.Equals(command, install, StringComparison.OrdinalIgnoreCase)) return $"it starts {command}, not {install}";
        if (Unsafe(command) is { } open) return $"someone other than an administrator may change {open}";
        if (Value(actions[0], "Arguments") != Arguments) return "its arguments are not OmniControl's";
        if (task.Descendants(Schema + "Triggers").Elements().Any()) return "it has a trigger";
        var principals = task.Descendants(Schema + "Principal").ToList();
        if (principals.Count != 1 || Value(principals[0], "LogonType") != LogonType || Value(principals[0], "RunLevel") != RunLevel)
            return "it does not run as the signed-in user at the highest level";
        if (install is not null && (SessionUser() is not { } user || Account(Value(principals[0], "UserId")) is not { } owner || !user.Equals(owner)))
            return "it is not for the user signed in here";
        return null;
    }

    private static string Value(XElement parent, string name) => parent.Element(Schema + name)?.Value.Trim() ?? string.Empty;

    /// A task's user as Windows stores it (a SID, or a name), as a SID; null when Windows does not know it.
    private static SecurityIdentifier? Account(string user)
    {
        try
        {
            return user.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)
                ? new SecurityIdentifier(user)
                : (SecurityIdentifier)new NTAccount(user).Translate(typeof(SecurityIdentifier));
        }
        catch (Exception ex) when (ex is ArgumentException or IdentityNotMappedException || ex.GetType() == typeof(SystemException)) // Windows' lookup failed
        {
            return null;
        }
    }

    /// The user signed in to this process's session, whoever this process runs as (an administrator's account when another one answered
    /// Windows' prompt); null when Windows does not say.
    private static SecurityIdentifier? SessionUser()
    {
        var (domain, user) = (SessionText(SessionNative.InfoDomainName), SessionText(SessionNative.InfoUserName));
        return string.IsNullOrEmpty(user) ? null : Account(string.IsNullOrEmpty(domain) ? user : $@"{domain}\{user}");
    }

    private static string? SessionText(int what)
    {
        if (!SessionNative.WTSQuerySessionInformationW(IntPtr.Zero, SessionNative.CurrentSession, what, out var buffer, out _)) return null;
        try
        {
            return Marshal.PtrToStringUni(buffer);
        }
        finally
        {
            SessionNative.WTSFreeMemory(buffer);
        }
    }

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

    /// Runs the task with these arguments when it starts <paramref name="exe"/> and only administrators can change that; false when there is none for it or Windows refused.
    public static bool Run(string exe, IEnumerable<string> arguments) => Unsafe(exe) is null && Mine(exe, task => Release(((dynamic)task).Run(Elevation.Quoted(arguments))));

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

    private static string Definition(string exe, SecurityIdentifier user) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="{Schema}">
          <RegistrationInfo><Description>Starts OmniControl as administrator without Windows' prompt (its Run as administrator setting).</Description></RegistrationInfo>
          <Principals><Principal id="Author"><UserId>{user.Value}</UserId><LogonType>{LogonType}</LogonType><RunLevel>{RunLevel}</RunLevel></Principal></Principals>
          <Settings>
            <MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Priority>5</Priority>
          </Settings>
          <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(exe)}</Command><Arguments>{Arguments}</Arguments><WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe))}</WorkingDirectory></Exec></Actions>
        </Task>
        """;
}
