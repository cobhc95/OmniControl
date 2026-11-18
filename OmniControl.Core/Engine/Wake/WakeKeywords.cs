namespace OmniControl.Core.Engine.Wake;

/// <summary>
/// The driver keywords that hold a card's wake settings, in one place for the reader and the fixer. The standard names come first; vendors that use
/// their own are found by what the keyword's description under Ndi\Params says ("Wake on Magic Packet"), so no brand is special-cased.
/// </summary>
internal static class WakeKeywords
{
    public static readonly string[] MagicNames = ["*WakeOnMagicPacket", "WakeOnMagicPacket", "WakeOnLan", "*WakeOnLan", "WolMagicPacket", "EnableWakeOnLan", "WakeFromS5", "S5WakeOnLan"];

    public static readonly string[] PatternNames = ["*WakeOnPattern", "WakeOnPattern"];

    public const string MagicDescription = "magic packet|wake.?on.?lan|wake.?from.?(s5|shutdown)|\\bwol\\b";

    public const string PatternDescription = "pattern";

    public const string AdapterClass = "{4d36e972-e325-11ce-bfc1-08002be10318}";
}
