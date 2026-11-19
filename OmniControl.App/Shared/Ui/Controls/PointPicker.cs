using OmniControl.Core.Engine.Screenshot;

namespace OmniControl.App.Shared.Ui.Controls;

/// THE screen point picker: a dimmed, crosshair overlay on every monitor with a small label of the
/// x, y under the pointer; a click picks, Esc or the right button cancels. Any action that needs a place on the screen asks it through
/// <see cref="PickAsync"/> (and stores the answer with <see cref="ScreenPointText"/>); nothing else opens such an overlay.
public static class PointPicker
{
    private static bool _open;

    /// Shows the overlays and returns the picked point in physical pixels on the virtual desktop, or null when cancelled (or one is already open). UI thread.
    public static async Task<(int X, int Y)?> PickAsync()
    {
        if (_open || ScreenCapture.Monitors().Count == 0) return null;
        _open = true;
        var done = new TaskCompletionSource<PixelPoint?>();
        var windows = ScreenCapture.Monitors().Select(m => new PointPickerOverlay(m.Bounds)).ToList();
        void Moved(PointPickerOverlay under)
        {
            foreach (var window in windows)
                if (window != under) window.ShowLabel(null, string.Empty);
        }
        try
        {
            foreach (var window in windows)
            {
                window.Moved += Moved;
                window.Finished += point => done.TrySetResult(point);
                window.Show();
            }
            var underPointer = ScreenCapture.MonitorUnderPointer();
            (windows.FirstOrDefault(w => w.Monitor == underPointer) ?? windows[0]).TakeFocus();
            return await done.Task is { } picked ? (picked.X, picked.Y) : null;
        }
        finally
        {
            foreach (var window in windows)
            {
                window.Moved -= Moved;
                window.Close();
            }
            _open = false;
        }
    }
}
