using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OmniControl.App.Audio;
using OmniControl.App.Controls;
using OmniControl.App.Osd;
using OmniControl.App.Services;
using OmniControl.Core.Models;

namespace OmniControl.App.Pages.Osd;

/// <summary>
/// The OSD placement window (docs/mockups/osd-custom-position.html): which screen to look at, the anchor, the two offsets and a name. While it
/// is open an <see cref="OsdPreview"/> shows the place live on that screen; sliding, typing, dragging the preview or nudging it all move the same
/// two numbers. OK keeps the place under its name and makes it the one in use (<see cref="Save"/>, called by whoever opened the window).
/// </summary>
public sealed partial class OsdPlacementViewModel : ObservableObject, IDisposable
{
    private const string Unit = " px", NewName = "My place";

    private readonly OsdService _osd;
    private readonly OsdPreview _preview;
    private readonly IReadOnlyList<Screen> _screens;
    private readonly MenuChoiceItem _screenChoice;
    private readonly AnchorItem _anchor;
    private readonly SliderItem _across, _away;
    private readonly TextItem _name;
    private Screen _screen;
    private OsdSpot _spot;

    public string Heading => "OSD position";

    /// <summary>The window's cards: screen, anchor, the two offsets, name.</summary>
    public IReadOnlyList<object> Cards { get; }

    /// <summary>The name can be kept: it is a valid name (a place with the same name is replaced).</summary>
    [ObservableProperty] private bool _canSave;

    /// <summary>Why OK is dimmed; empty while it is not.</summary>
    [ObservableProperty] private string _problem = string.Empty;

    public OsdPlacementViewModel(OsdService osd)
    {
        _osd = osd;
        _screens = osd.AllScreens;
        _screen = OsdScreens.Targets(_screens, osd.Settings)[0];
        _spot = osd.Settings.CurrentSpot();
        _preview = osd.MakePreview(OnMoved);

        _screenChoice = new MenuChoiceItem("Screen", () => OsdScreens.NameOf(_screens, _screen), ScreenRows) { Tip = "The screen the preview is shown on" };
        _anchor = new AnchorItem("Anchor", (int)_spot.Anchor, OnAnchor);
        _across = Offset(OsdPlacement.AcrossLabel(_spot.Anchor), _spot.Across, v => _spot = _spot with { Across = v });
        _away = Offset(OsdPlacement.AwayLabel(_spot.Anchor), _spot.Away, v => _spot = _spot with { Away = v });
        _name = new TextItem("Name", FirstFreeName(osd), OnName) { Hint = "Name this place" };
        Cards = new object[] { _screenChoice, _anchor, _across, _away, _name };
        CheckName(_name.Text);
        _preview.Show(_screen, _spot);
    }

    private static string FirstFreeName(OsdService osd) => PresetRules.FirstFree(NewName, osd.Places.Names);

    private SliderItem Offset(string label, int value, Action<int> set) => new(label, value, v =>
    {
        set(v);
        _preview.Show(_screen, _spot);
        return OperationResult.Ok();
    }, _ => { }, -Timing.OsdOffsetMax, Timing.OsdOffsetMax, Unit, 0);

    private IReadOnlyList<MenuRowItem> ScreenRows() => _screens.Select(s =>
        new MenuRowItem(OsdScreens.NameOf(_screens, s), string.Empty, ReferenceEquals(s, _screen), string.Empty, new RelayCommand(() => ChooseScreen(s)))).ToList();

    private void ChooseScreen(Screen screen)
    {
        _screen = screen;
        _preview.Show(screen, _spot);
        _screenChoice.Refresh();
    }

    // The same place on screen, pinned to another anchor: the offsets and their names change, the preview does not move.
    private void OnAnchor(int index)
    {
        _preview.Show(_screen, _spot);
        _spot = _preview.Repin((OsdAnchor)index);
        ShowSpot();
    }

    // The preview was dragged or nudged: the sliders follow.
    private void OnMoved(OsdSpot spot)
    {
        _spot = spot;
        ShowSpot();
    }

    private void ShowSpot()
    {
        _across.Label = OsdPlacement.AcrossLabel(_spot.Anchor);
        _away.Label = OsdPlacement.AwayLabel(_spot.Anchor);
        _across.Load(_spot.Across);
        _away.Load(_spot.Away);
        _anchor.Load((int)_spot.Anchor);
    }

    private void OnName(string text) => CheckName(text);

    private void CheckName(string text)
    {
        var problem = PresetRules.Error(text);
        (CanSave, Problem) = (problem is null, problem ?? string.Empty);
    }

    /// <summary>Keeps the place under its name and makes it the one the OSD uses (a place with that name is replaced).</summary>
    public void Save() => _osd.UsePlace(_osd.Places.Save(_name.Text, _spot).Id);

    public void Dispose() => _preview.Dispose();
}
