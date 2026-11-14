using System.Diagnostics;
using OmniControl.App.Audio;
using OmniControl.Core.Engine;

namespace OmniControl.Tests;

/// <summary>
/// The process side of per-program routing, without asking Windows to move anything: Windows answers E_INVALIDARG (0x80070057)
/// for a process that is not running, so such a process must be skipped, never reported as a refusal.
/// </summary>
public class ProgramRoutingTests
{
    /// <summary>A process id that no running process has (ids are multiples of 4; an odd one is never given out).</summary>
    private const int NoProcess = 999_999;

    [Fact]
    public void IsRunning_TellsARunningProcessFromAnEndedOne()
    {
        Assert.True(ProcessTree.IsRunning(Environment.ProcessId));
        Assert.False(ProcessTree.IsRunning(NoProcess));

        using var ended = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
        ended.WaitForExit();
        Assert.False(ProcessTree.IsRunning(ended.Id)); // our handle keeps the ended process's object alive: it still opens
    }

    /// <summary>
    /// Regression: the path ended in "...79d4" (not the render interface class), so Windows answered 0x80070057 to every move, even of a
    /// playing process, while clearing worked; with "...79d2" a playing test process was moved (S_OK, read back as this path).
    /// </summary>
    [Fact]
    public void RenderInterfacePath_UsesTheAudioRenderInterfaceClass()
    {
        Assert.Equal(@"\\?\SWD#MMDEVAPI#{0.0.0.00000000}.{1e2139d3-cd74-466f-804d-14f389aeb0df}#{e6327cad-dcec-4949-ae8a-991e976a79d2}",
            CoreAudio.RenderInterfacePath(OwnerPc.Cable16));
    }

    /// <summary>
    /// Regression: when a program that followed the master gets effects, its run holds it on its own cable first and the master bus
    /// lets go of it afterwards; that late release used to undo the run's hold (program back on the bus's cable, dry).
    /// </summary>
    [Fact]
    public void BusLettingGoLate_DoesNotUndoTheProgramRunsHold()
    {
        var holds = new RoutingHolds();
        holds.Hold(OwnerPc.Spotify, OwnerPc.CableInput); // the bus holds Spotify
        holds.Hold(OwnerPc.Spotify, OwnerPc.CableA);     // Spotify's own run takes it
        Assert.False(holds.Release(OwnerPc.Spotify, OwnerPc.CableInput)); // the bus lets go late: nothing changes
        Assert.Equal(OwnerPc.CableA, holds.DeviceOf(OwnerPc.Spotify));
        Assert.True(holds.Release(OwnerPc.Spotify, OwnerPc.CableA));      // the run stops: released
        Assert.False(holds.IsHeld(OwnerPc.Spotify));
    }

    [Fact]
    public void ProgramWhoseProcessesEnded_IsNotARefusal()
    {
        Assert.True(CoreAudio.TrySetProgramDevice(new[] { NoProcess }, "{0.0.0.00000000}.{00000000-0000-0000-0000-000000000000}", out var error));
        Assert.Equal(string.Empty, error);
    }
}
