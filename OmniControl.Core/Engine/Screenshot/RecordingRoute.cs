namespace OmniControl.Core.Engine.Screenshot;

/// <summary>
/// The ways a recording can be made, best first: the graphics card's encoder fed by the graphics-card screen copy on the adapter that drives
/// the monitor; a graphics card's encoder (another adapter's when there is one) fed by the GDI copy; Windows' software encoder; a GIF has
/// its one way. Stored by number in the settings: never reorder.
/// </summary>
public enum RecordingStep { GraphicsCard, OtherEncoder, Software, Gif }

/// <summary>
/// The way to start recording with on a PC whose graphics adapters and drivers give <see cref="MachineKey"/>: remembered after a fallback
/// worked (or after a way hung), so the next recording goes straight to it; a different key (a new driver, a card added) tries the
/// graphics card again.
/// </summary>
public sealed record RecordingRoute(string MachineKey, RecordingStep Step);

/// <summary>The chain of ways for a recording (<see cref="RecordingStep"/>), where it starts, and their words.</summary>
internal static class RecordingSteps
{
    private static readonly RecordingStep[] HardwareChain = { RecordingStep.GraphicsCard, RecordingStep.OtherEncoder, RecordingStep.Software };

    /// <summary>The ways to try, in order: the GIF's own, software only when the hardware encoder is off, else all three.</summary>
    public static IReadOnlyList<RecordingStep> Chain(VideoFormat format, bool hardware) =>
        format == VideoFormat.Gif ? new[] { RecordingStep.Gif } : hardware ? HardwareChain : new[] { RecordingStep.Software };

    /// <summary>Where in <paramref name="chain"/> to start: at the remembered way when it was remembered on this same set of drivers, else at the top.</summary>
    public static int StartIndex(IReadOnlyList<RecordingStep> chain, RecordingRoute? remembered, string machineKey, RecordingLog log)
    {
        if (remembered is null) return 0;
        if (remembered.MachineKey != machineKey)
        {
            log.Line($"remembered way {remembered.Step} was for other graphics drivers ({remembered.MachineKey}); trying from the top again");
            return 0;
        }
        var index = Math.Max(0, IndexOf(chain, remembered.Step));
        log.Line($"remembered way on these drivers: {remembered.Step}; starting at {chain[index]}");
        return index;
    }

    private static int IndexOf(IReadOnlyList<RecordingStep> chain, RecordingStep step)
    {
        for (var i = 0; i < chain.Count; i++)
            if (chain[i] == step) return i;
        return -1;
    }

    /// <summary>What to remember after <paramref name="worked"/> recorded fine: nothing when it is the top of a hardware chain, the way otherwise; unchanged off the hardware chain.</summary>
    public static RecordingRoute? AfterSuccess(IReadOnlyList<RecordingStep> chain, RecordingStep worked, string machineKey, RecordingRoute? remembered) =>
        chain.Count < 2 ? remembered : worked == chain[0] ? null : new RecordingRoute(machineKey, worked);

    /// <summary>What to remember after <paramref name="hung"/> did not finish its file: the next way, when there is one.</summary>
    public static RecordingRoute? AfterHang(IReadOnlyList<RecordingStep> chain, RecordingStep hung, string machineKey, RecordingRoute? remembered)
    {
        var next = IndexOf(chain, hung) + 1;
        return chain.Count < 2 || next <= 0 || next >= chain.Count ? remembered : new RecordingRoute(machineKey, chain[next]);
    }

    /// <summary>The way in the person's words, for the status line.</summary>
    public static string Words(RecordingStep step) => step switch
    {
        RecordingStep.GraphicsCard => "the graphics card's encoder",
        RecordingStep.OtherEncoder => "the graphics card's encoder with a slower screen copy",
        RecordingStep.Software => "the software encoder",
        _ => "the GIF writer",
    };
}

/// <summary>The watchdog's limits (<see cref="CoreTiming"/>'s by default; shorter in tests).</summary>
internal sealed record RecordingWaits(int OpenMs, int FirstFrameMs, int StuckMs, int FinishMs)
{
    public static readonly RecordingWaits Default =
        new(CoreTiming.RecordOpenWaitMs, CoreTiming.RecordFirstFrameWaitMs, CoreTiming.RecordStuckMs, CoreTiming.RecordFinishWaitMs);
}

/// <summary>What a test makes a way do instead of working.</summary>
internal enum RecordingFault { Fail, HangOpen, HangWrite, HangFinish, Silent }

/// <summary>
/// Test-only faults: a way made to fail, hang or encode nothing, so each fallback can be forced on a PC where every way works, and the adapter
/// <see cref="RecordingStep.OtherEncoder"/> must use. A hang lasts until <see cref="Release"/> is set. The app never makes one.
/// </summary>
internal sealed class RecordingFaults
{
    public Dictionary<RecordingStep, RecordingFault> At { get; } = new();

    public long? Adapter { get; init; }

    public ManualResetEventSlim Release { get; } = new(false);
}
