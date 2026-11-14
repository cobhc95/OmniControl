using CommunityToolkit.Mvvm.ComponentModel;
using OmniControl.App.Services;

namespace OmniControl.App.Pages.Volume;

/// <summary>What the Outputs header offers about its programs: "Show closed" (kept between runs) and the "Add program" pop-down.</summary>
public sealed partial class ProgramListTools : ObservableObject
{
    private readonly Action _changed;

    /// <param name="changed">Called after "Show closed" changed, so the page lists its programs again.</param>
    public ProgramListTools(AddProgramMenuViewModel add, Action changed)
    {
        Add = add;
        _changed = changed;
        _showClosed = AppSettings.Current.MixerShowClosed;
    }

    public AddProgramMenuViewModel Add { get; }

    /// <summary>Remembered programs that are not open are listed too (dimmed, "Not open").</summary>
    [ObservableProperty] private bool _showClosed;

    partial void OnShowClosedChanged(bool value)
    {
        AppSettings.Current.MixerShowClosed = value;
        AppSettings.Current.Save();
        _changed();
    }
}
