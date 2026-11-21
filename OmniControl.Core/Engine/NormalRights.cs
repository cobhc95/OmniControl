using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;
using static OmniControl.Core.Native.NormalRightsNative;

namespace OmniControl.Core.Engine;

/// THE way OmniControl starts what the user asked for (programs, files, addresses, commands, scripts, tools it calls): always with the
/// signed-in user's normal rights. While OmniControl runs as administrator a program it started would be elevated too, so then the
/// program is started with the token of Windows' shell (Explorer, the user's own unelevated session), and what opens through the shell
/// (an address, a document, a shortcut) is handed to a copy of OmniControl started that way (<see cref="OpenArgument"/>), which opens it
/// and ends. Without the shell (Explorer not running) nothing is started rather than started elevated.
public static class NormalRights
{
    /// The argument of the short-lived normal-rights copy that opens one thing through the shell: <c>--open-as-user target arguments folder window-style</c>.
    public const string OpenArgument = "--open-as-user";

    private const int NoShell = 1168; // ERROR_NOT_FOUND
    private static readonly bool Elevated = Elevation.IsElevated();

    /// A program that was started: the process and, when asked for, its output and error text (read them to the end so it never blocks).
    public sealed class Started : IDisposable
    {
        internal Started(Process process, StreamReader? output, StreamReader? errors) => (Process, Output, Errors) = (process, output, errors);

        public Process Process { get; }
        public StreamReader? Output { get; }
        public StreamReader? Errors { get; }

        public void Dispose()
        {
            Output?.Dispose();
            Errors?.Dispose();
            Process.Dispose();
        }
    }

    /// Starts <paramref name="file"/> with <paramref name="arguments"/> (each passed as is). <paramref name="environment"/> adds variables
    /// for this run only. Throws <see cref="Win32Exception"/> or <see cref="InvalidOperationException"/> when it could not be started.
    public static Started Start(string file, IReadOnlyList<string> arguments, bool hidden, bool readOutput,
        IReadOnlyDictionary<string, string>? environment = null, string? directory = null)
    {
        if (Elevated) return StartLowered(file, arguments, hidden, readOutput, environment, directory);
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = hidden,
            RedirectStandardOutput = readOutput,
            RedirectStandardError = readOutput,
        };
        if (!string.IsNullOrEmpty(directory)) start.WorkingDirectory = directory;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        foreach (var (name, value) in environment ?? new Dictionary<string, string>()) start.Environment[name] = value;
        var process = Process.Start(start) ?? throw new InvalidOperationException("Windows did not start it.");
        return new Started(process, readOutput ? process.StandardOutput : null, readOutput ? process.StandardError : null);
    }

    /// Opens a program, file, folder or address the way the shell does (a document in its app, a shortcut's target, a web page in the
    /// browser). Throws <see cref="Win32Exception"/> or <see cref="InvalidOperationException"/> when Windows refused at once; while
    /// OmniControl is administrator, a failure after the hand-over is not seen here.
    public static void Open(string target, string arguments = "", string directory = "", ProcessWindowStyle style = ProcessWindowStyle.Normal)
    {
        if (Elevated)
        {
            using var _ = Start(Environment.ProcessPath ?? throw new InvalidOperationException("OmniControl's own path is unknown."),
                new[] { OpenArgument, target, arguments, directory, style.ToString() }, hidden: true, readOutput: false);
            return;
        }
        using var opened = Process.Start(ShellStart(target, arguments, directory, style));
    }

    /// The copy <see cref="Open"/> started: opens the one thing and ends (0 opened, 1 not); null when the arguments ask something else.
    public static int? FromArguments(string[] args)
    {
        if (args.Length != 5 || args[0] != OpenArgument) return null;
        if (Elevated || !Enum.TryParse<ProcessWindowStyle>(args[4], out var style)) return 1; // never opens anything elevated
        try
        {
            using var opened = Process.Start(ShellStart(args[1], args[2], args[3], style));
            return 0;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return 1;
        }
    }

    private static ProcessStartInfo ShellStart(string target, string arguments, string directory, ProcessWindowStyle style) =>
        new(target) { UseShellExecute = true, Arguments = arguments, WorkingDirectory = directory, WindowStyle = style };

    private static Started StartLowered(string file, IReadOnlyList<string> arguments, bool hidden, bool readOutput,
        IReadOnlyDictionary<string, string>? environment, string? directory)
    {
        using var token = ShellToken();
        var command = new StringBuilder(Elevation.Quoted(arguments.Prepend(file)));
        var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>(), lpDesktop = @"winsta0\default" };
        AnonymousPipeServerStream? output = null, errors = null;
        var block = IntPtr.Zero;
        var done = false;
        try
        {
            if (readOutput)
            {
                // Windows' secondary logon hands the inheritable pipe ends to the new process as its standard handles.
                output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
                errors = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
                startup.dwFlags = StartfUseStdHandles;
                startup.hStdOutput = output.ClientSafePipeHandle.DangerousGetHandle();
                startup.hStdError = errors.ClientSafePipeHandle.DangerousGetHandle();
            }
            block = EnvironmentFor(token, environment);
            var flags = CreateSuspended | CreateUnicodeEnvironment | (hidden ? CreateNoWindow : 0);
            if (!CreateProcessWithTokenW(token, 0, null, command, flags, block, string.IsNullOrEmpty(directory) ? null : directory, ref startup, out var info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Windows didn't start {Path.GetFileName(file)} with normal rights");
            try
            {
                // Taken while it is still suspended, so it cannot end before its handle is held (the exit code stays readable).
                var process = Process.GetProcessById(info.dwProcessId);
                _ = process.Handle;
                ResumeThread(info.hThread);
                done = true;
                return new Started(process, output is null ? null : new StreamReader(output), errors is null ? null : new StreamReader(errors));
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException("Windows did not start it.", ex);
            }
            finally
            {
                if (!done) TerminateProcess(info.hProcess, 1);
                CloseHandle(info.hThread);
                CloseHandle(info.hProcess);
            }
        }
        finally
        {
            output?.DisposeLocalCopyOfClientHandle();
            errors?.DisposeLocalCopyOfClientHandle();
            if (!done)
            {
                output?.Dispose();
                errors?.Dispose();
            }
            if (block != IntPtr.Zero) Marshal.FreeHGlobal(block);
        }
    }

    // The token of Windows' shell, the signed-in user's own session without elevation, as a primary token for a new process.
    private static SafeAccessTokenHandle ShellToken()
    {
        var shell = GetShellWindow();
        if (shell == IntPtr.Zero || GetWindowThreadProcessId(shell, out var pid) == 0)
            throw new Win32Exception(NoShell, "Windows' shell (Explorer) is not running, so nothing was started");
        using var process = OpenProcess(ProcessQueryLimited, false, pid);
        if (process.IsInvalid || !OpenProcessToken(process, TokenDuplicate, out var token)) throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            if (!DuplicateTokenEx(token, TokenQuery | TokenDuplicate | TokenAssignPrimary | TokenAdjustDefault | TokenAdjustSessionId, IntPtr.Zero,
                    SecurityImpersonation, TokenPrimary, out var primary))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return primary;
        }
    }

    // The user's own environment (their profile's, not the administrator's) with the extra variables; the caller frees it.
    private static IntPtr EnvironmentFor(SafeAccessTokenHandle token, IReadOnlyDictionary<string, string>? extra)
    {
        var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!CreateEnvironmentBlock(out var block, token, false)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            for (var at = block; ;)
            {
                var entry = Marshal.PtrToStringUni(at) ?? string.Empty;
                if (entry.Length == 0) break;
                var equals = entry.IndexOf('=', 1); // per-drive entries ("=C:=C:\") start with '='
                if (equals > 0) variables[entry[..equals]] = entry[(equals + 1)..];
                at += (entry.Length + 1) * sizeof(char);
            }
        }
        finally
        {
            DestroyEnvironmentBlock(block);
        }
        foreach (var (name, value) in extra ?? new Dictionary<string, string>()) variables[name] = value;
        return Marshal.StringToHGlobalUni(string.Concat(variables.Select(v => $"{v.Key}={v.Value}\0")) + "\0");
    }
}
