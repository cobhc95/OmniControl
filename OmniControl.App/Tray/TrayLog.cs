using OmniControl.App.Services;

namespace OmniControl.App.Tray;

/// <summary><c>tray-log.txt</c>: how long the quick panel took from the click on the tray icon to being shown (a debugging aid, only in the log).</summary>
internal sealed class TrayLog
{
    private const string FileName = "tray-log.txt";

    public void Write(string line) => AppDataStore.AppendLog(FileName, line);
}
