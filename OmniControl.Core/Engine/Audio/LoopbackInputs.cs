namespace OmniControl.Core.Engine;

/// Recognises recording devices that record what the PC plays rather than a microphone (reads names only).
public static class LoopbackInputs
{
    /// The one list of names sound drivers give such a device (case ignored): Realtek's "Stereo Mix" (also "Stereomix" and its Spanish
    /// and French names), Creative's "What U Hear", older drivers' "Wave Out Mix".
    private static readonly string[] Names = { "Stereo Mix", "Stereomix", "Mezcla estéreo", "Mixage stéréo", "What U Hear", "Wave Out Mix" };

    public static bool RecordsPlayback(string deviceName) => Names.Any(n => deviceName.Contains(n, StringComparison.OrdinalIgnoreCase));
}
