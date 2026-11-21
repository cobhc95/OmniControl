namespace OmniControl.Core.Engine;

/// Windows' own tools by their full path in the System32 folder, never found through the search path (a program of the same name in
/// the current folder or a user-writable PATH entry would otherwise run, elevated when OmniControl is).
public static class SystemTools
{
    public static string Cmd => In("cmd.exe");
    public static string PowerShell => In(@"WindowsPowerShell\v1.0\powershell.exe");
    public static string Shutdown => In("shutdown.exe");
    public static string Netsh => In("netsh.exe");
    public static string Sc => In("sc.exe");
    public static string PowerCfg => In("powercfg.exe");
    public static string NetCfg => In("netcfg.exe");

    /// The System32 folder (a working folder that a user cannot fill with DLLs).
    public static string Folder => Environment.SystemDirectory;

    private static string In(string name) => Path.Combine(Environment.SystemDirectory, name);
}
