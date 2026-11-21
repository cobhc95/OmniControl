namespace OmniControl.App.Features.Volume.Pages;

// A microphone row's Listen button: Windows' own "Listen to this device" (VolumeControls sets it, with its state and actions); the row shows
// what Windows reports, read on every scan and after each change.
public sealed partial class VolumePageViewModel
{
    /// Reads Windows' Listen setting of one microphone into its row.
    private void SyncListen(InputItem item)
    {
        if (_controls.ReadListen(item.Id) is { } listen) item.SyncListen(listen.On, listen.Output);
    }

    private void OnListenChanged(string deviceId)
    {
        if (_inputs.TryGetValue(deviceId, out var row)) SyncListen(row);
    }
}
