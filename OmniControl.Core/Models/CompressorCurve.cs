namespace OmniControl.Core.Models;

/// <summary>
/// The compressor's static gain computer: the output level for an input level with a threshold, a ratio and a soft knee. The compressor
/// stage (what the sound goes through) and its transfer-curve graph call this one function, so the picture is what the sound does.
/// From TabForge's CompressorCurve (MIT, see NOTICE.txt).
/// </summary>
public static class CompressorCurve
{
    /// <summary>The output level in dB for an input level in dB: unchanged below the knee, <c>threshold + over / ratio</c> above it, a quadratic blend inside it.</summary>
    public static double OutputDb(double inDb, double thresholdDb, double ratio, double kneeDb)
    {
        var over = inDb - thresholdDb;
        if (2 * over < -kneeDb) return inDb;
        if (kneeDb > 0 && 2 * Math.Abs(over) <= kneeDb)
        {
            var t = over + kneeDb / 2;
            return inDb + (1 / ratio - 1) * t * t / (2 * kneeDb);
        }
        return thresholdDb + over / ratio;
    }

    /// <summary>The gain reduction in dB (0 or more) for an input level in dB.</summary>
    public static double ReductionDb(double inDb, double thresholdDb, double ratio, double kneeDb) =>
        Math.Max(0, inDb - OutputDb(inDb, thresholdDb, ratio, kneeDb));
}
