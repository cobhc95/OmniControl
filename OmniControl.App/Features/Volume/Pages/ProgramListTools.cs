namespace OmniControl.App.Features.Volume.Pages;

/// What the Outputs header offers about its programs: "Show closed" (kept between runs) and the "Add program" pop-down.
public sealed partial class ProgramListTools : ObservableObject
{
    private readonly Action _changed;
    private readonly ISettingSlot<bool> _showClosedSetting;

    /// <param name="changed">Called after "Show closed" changed, so the page lists its programs again.</param>
    /// <param name="showClosed">The setting that keeps "Show closed".</param>
    public ProgramListTools(AddProgramMenuViewModel add, Action changed, ISettingSlot<bool> showClosed)
    {
        Add = add;
        _changed = changed;
        _showClosedSetting = showClosed;
        _showClosed = showClosed.Value;
    }

    public AddProgramMenuViewModel Add { get; }

    /// Remembered programs that are not open are listed too (dimmed, "Not open").
    [ObservableProperty] private bool _showClosed;

    partial void OnShowClosedChanged(bool value)
    {
        _showClosedSetting.Value = value;
        _changed();
    }
}
