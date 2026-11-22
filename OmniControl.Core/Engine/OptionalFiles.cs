using OmniControl.Core.Engine.Apo;
using OmniControl.Core.Engine.Resources;

namespace OmniControl.Core.Engine;

/// The files Setup leaves out with a feature that was not ticked when "Don't install them" is chosen (tools/setup.iss retypes them): while
/// one is missing its feature is not installed, so nothing touches the assembly or driver that is not there.
public static class OptionalFiles
{
    public const string Sensors = "LibreHardwareMonitorLib.dll", QrCodes = "zxing.dll";
    public static readonly string EqualizerApo = Path.Combine(ApoInstaller.Folder, ApoInstaller.FileName);
    public static readonly string PacketFilter = Path.Combine(PacketFilterSetup.Folder, PacketFilterSetup.Inf);

    public static bool Present(string file) => File.Exists(Path.Combine(AppContext.BaseDirectory, file));
}
