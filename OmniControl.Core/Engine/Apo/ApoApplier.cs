using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using Microsoft.Win32;

namespace OmniControl.Core.Engine.Apo;

/// <summary>
/// Carries out an <see cref="ApoPlan"/> (needs administrator rights). Used only by OmniControl's elevated setup mode
/// (<c>OmniControl.exe --engine-setup install|uninstall</c>), never by the running app. An install that fails half way is undone
/// with the uninstall plan, which puts back what was saved first.
/// </summary>
public static class ApoApplier
{
    private const string AudioService = "AudioSrv";

    /// <summary>Runs every step in order; stops at the first failure and returns its message (null = all done).</summary>
    public static string? Apply(ApoPlan plan, Action<string> log)
    {
        if (!plan.CanApply) return "The plan has problems and was not run.";
        foreach (var step in plan.Steps)
        {
            log("Doing: " + step.Description);
            try
            {
                Run(step);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or Win32Exception
                                           or InvalidOperationException or System.ServiceProcess.TimeoutException or ArgumentException)
            {
                log($"Failed: {ex.Message}");
                return $"{step.Description} failed: {ex.Message}";
            }
        }
        log("Done.");
        return null;
    }

    private static void Run(ApoStep step)
    {
        switch (step.Kind)
        {
            case ApoStepKind.SetString:
                WithKey(step.Target, key => key.SetValue(step.Name, (string)step.Value!, RegistryValueKind.String));
                break;
            case ApoStepKind.SetMultiString:
                WithKey(step.Target, key => key.SetValue(step.Name, (string[])step.Value!, RegistryValueKind.MultiString));
                break;
            case ApoStepKind.SetDword:
                WithKey(step.Target, key => key.SetValue(step.Name, (int)step.Value!, RegistryValueKind.DWord));
                break;
            case ApoStepKind.DeleteValue:
                WithKey(step.Target, key => key.DeleteValue(step.Name!, throwOnMissingValue: false));
                break;
            case ApoStepKind.DeleteKey:
                using (var machine = Machine()) machine.DeleteSubKeyTree(step.Target, throwOnMissingSubKey: false);
                break;
            case ApoStepKind.CopyFile:
                Directory.CreateDirectory(Path.GetDirectoryName(step.Target)!);
                File.Copy(step.Name!, step.Target, overwrite: true);
                break;
            case ApoStepKind.WriteFile:
                File.WriteAllText(step.Target, (string)step.Value!);
                break;
            case ApoStepKind.CreateSharedFolder:
                CreateSharedFolder(step.Target);
                break;
            case ApoStepKind.DeleteFolder:
                if (Directory.Exists(step.Target)) Directory.Delete(step.Target, recursive: true);
                break;
            case ApoStepKind.RegisterDll:
                RunRegsvr32($"/s \"{step.Target}\"");
                break;
            case ApoStepKind.UnregisterDll:
                RunRegsvr32($"/u /s \"{step.Target}\"");
                break;
            case ApoStepKind.RestartAudioService:
                RestartAudioService();
                break;
        }
    }

    private static RegistryKey Machine() => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);

    private static void WithKey(string path, Action<RegistryKey> change)
    {
        using var machine = Machine();
        using var key = machine.CreateSubKey(path, writable: true)
                        ?? throw new UnauthorizedAccessException($"Couldn't open HKLM\\{path} for writing.");
        change(key);
    }

    /// <summary>The audio service (LocalService) reads the folder, the signed-in user (OmniControl) writes the config in it.</summary>
    private static void CreateSharedFolder(string path)
    {
        var folder = Directory.CreateDirectory(path);
        var security = folder.GetAccessControl();
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        folder.SetAccessControl(security);
    }

    private static void RunRegsvr32(string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo("regsvr32.exe", arguments) { UseShellExecute = false, CreateNoWindow = true })
                            ?? throw new InvalidOperationException("regsvr32 did not start.");
        if (!process.WaitForExit(CoreTiming.ElevatedHelperTimeoutMs))
        {
            process.Kill();
            throw new InvalidOperationException($"regsvr32 {arguments} did not finish in time.");
        }
        if (process.ExitCode != 0) throw new InvalidOperationException($"regsvr32 {arguments} returned {process.ExitCode}.");
    }

    private static void RestartAudioService()
    {
        using var service = new ServiceController(AudioService);
        var wait = TimeSpan.FromMilliseconds(CoreTiming.ElevatedHelperTimeoutMs / 4);
        if (service.Status != ServiceControllerStatus.Stopped)
        {
            service.Stop();
            service.WaitForStatus(ServiceControllerStatus.Stopped, wait);
        }
        service.Start();
        service.WaitForStatus(ServiceControllerStatus.Running, wait);
    }
}
